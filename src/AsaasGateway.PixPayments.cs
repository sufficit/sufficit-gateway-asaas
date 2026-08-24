using Microsoft.Extensions.Logging;
using Sufficit.Finance;
using Sufficit.Gateway;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Sufficit.Gateway.Asaas;

public sealed partial class AsaasGateway : IPixPaymentGateway
{
    private static readonly HashSet<string> PixPaymentEvents = new(
        AsaasWebhookEventSets.PaymentLifecycle,
        StringComparer.Ordinal);

    string IPixPaymentGateway.ProviderCode => ProviderCodeValue;

    public async Task<PixPaymentResult> CreateAsync(
        PixPaymentRequest request,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ValidatePixRequest(request);
        ArgumentNullException.ThrowIfNull(context);

        var existing = await FindPixPaymentByReferenceAsync(
            request.PaymentId,
            context,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return existing;

        var customerId = await ResolvePixCustomerAsync(request, context, cancellationToken)
            .ConfigureAwait(false);
        var externalReference = PixExternalReference(request.PaymentId);
        var payload = new
        {
            customer = customerId,
            billingType = "PIX",
            value = request.Value,
            dueDate = request.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            description = request.Description.Trim(),
            externalReference
        };

        try
        {
            using var response = await SendGatewayAsync(
                () => CreateJsonRequest(HttpMethod.Post, BuildUri(context, "payments"), payload),
                context,
                cancellationToken).ConfigureAwait(false);
            await EnsurePixSuccessAsync(response, PixPaymentOperation.Create, null, cancellationToken)
                .ConfigureAwait(false);
            using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            var payment = ParsePixPayment(document.RootElement);
            ValidatePixReference(payment.ExternalReference, externalReference);
            return await AddPixPresentationAsync(payment.Result, context, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PixPaymentGatewayException exception)
            when (exception.Category == PixPaymentErrorCategory.AmbiguousResult)
        {
            _logger.LogWarning(
                exception,
                "Asaas returned an ambiguous Pix create result for payment {PaymentId}; reconciling by reference.",
                request.PaymentId);
            var reconciled = await TryFindPixPaymentAfterAmbiguousCreateAsync(
                request.PaymentId,
                context,
                cancellationToken).ConfigureAwait(false);
            if (reconciled is not null)
                return reconciled;
            throw;
        }
        catch (AsaasGatewayException exception)
        {
            throw MapPixTransportException(PixPaymentOperation.Create, null, exception);
        }
    }

    public async Task<PixPaymentResult?> GetAsync(
        string providerChargeId,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ValidatePixProviderChargeId(providerChargeId);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            using var response = await SendGatewayAsync(
                () => new HttpRequestMessage(
                    HttpMethod.Get,
                    BuildUri(context, $"payments/{Uri.EscapeDataString(providerChargeId)}")),
                context,
                cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            await EnsurePixSuccessAsync(
                response,
                PixPaymentOperation.Query,
                providerChargeId,
                cancellationToken).ConfigureAwait(false);
            using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            var payment = ParsePixPayment(document.RootElement, providerChargeId).Result;
            return await AddPixPresentationAsync(payment, context, cancellationToken).ConfigureAwait(false);
        }
        catch (AsaasGatewayException exception)
        {
            throw MapPixTransportException(PixPaymentOperation.Query, providerChargeId, exception);
        }
    }

    Task<bool> IPixPaymentGateway.AuthenticateWebhookAsync(
        IReadOnlyDictionary<string, string> requestHeaders,
        GatewayCallContext context,
        CancellationToken cancellationToken)
        => AuthenticateWebhookCoreAsync(requestHeaders, context, cancellationToken);

    PixPaymentNotification IPixPaymentGateway.ParseWebhook(string requestPayload)
    {
        if (string.IsNullOrWhiteSpace(requestPayload))
            throw new FormatException("The Asaas payment webhook payload is empty.");

        using var document = JsonDocument.Parse(requestPayload);
        var root = document.RootElement;
        var eventId = GetString(root, "id")?.Trim();
        var eventType = GetString(root, "event")?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(eventId)
            || eventId.Length > 255
            || string.IsNullOrWhiteSpace(eventType)
            || !PixPaymentEvents.Contains(eventType))
        {
            throw new FormatException("The Asaas payment webhook has an invalid event identity or type.");
        }

        if (!root.TryGetProperty("payment", out var payment)
            || payment.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("The Asaas payment webhook does not contain a payment object.");
        }

        var chargeId = GetString(payment, "id")?.Trim();
        if (string.IsNullOrWhiteSpace(chargeId)
            || chargeId.Length > 200
            || chargeId.Any(char.IsControl))
        {
            throw new FormatException("The Asaas payment webhook does not contain a valid charge identifier.");
        }

        var providerStatus = GetString(payment, "status")?.Trim().ToUpperInvariant()
            ?? EventPixStatus(eventType);
        return new PixPaymentNotification
        {
            EventId = eventId,
            EventType = eventType,
            EventAt = ParsePixDate(GetString(root, "dateCreated")),
            ChargeId = chargeId,
            ProviderStatus = providerStatus,
            Status = MapPixStatus(providerStatus),
            ExternalReference = NormalizeOptional(GetString(payment, "externalReference")),
            PaidAt = ParsePixDate(GetString(payment, "clientPaymentDate"))
                ?? ParsePixDate(GetString(payment, "paymentDate"))
                ?? ParsePixDate(GetString(payment, "confirmedDate")),
            Value = TryGetDecimal(payment, "value")
        };
    }

    private async Task<string> ResolvePixCustomerAsync(
        PixPaymentRequest request,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        var suppliedId = request.Payer.ProviderCustomerId?.Trim();
        if (!string.IsNullOrEmpty(suppliedId))
        {
            ValidatePixProviderCustomerId(suppliedId);
            return suppliedId;
        }

        var payerDocument = OnlyDigits(request.Payer.Document);
        var payerReference = PixCustomerReference(request.ContextId);
        var query = $"customers?cpfCnpj={Uri.EscapeDataString(payerDocument)}&externalReference={Uri.EscapeDataString(payerReference)}&limit=2";
        using (var searchResponse = await SendGatewayAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BuildUri(context, query)),
            context,
            cancellationToken).ConfigureAwait(false))
        {
            await EnsurePixSuccessAsync(searchResponse, PixPaymentOperation.Customer, null, cancellationToken)
                .ConfigureAwait(false);
            using var searchDocument = await ReadJsonAsync(searchResponse, cancellationToken).ConfigureAwait(false);
            var matches = GetDataArray(searchDocument.RootElement)
                .Where(value => string.Equals(OnlyDigits(GetString(value, "cpfCnpj")), payerDocument, StringComparison.Ordinal)
                    && string.Equals(GetString(value, "externalReference"), payerReference, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length > 1)
            {
                throw new PixPaymentGatewayException(
                    PixPaymentErrorCategory.SecurityBlock,
                    "asaas_duplicate_pix_customers",
                    "Multiple Asaas customers match the Pix payer reference.");
            }

            if (matches.Length == 1)
                return GetRequiredString(matches[0], "id");
        }

        var createPayload = new
        {
            name = request.Payer.Name.Trim(),
            cpfCnpj = payerDocument,
            email = NormalizeOptional(request.Payer.Email),
            mobilePhone = OnlyDigits(request.Payer.Phone),
            externalReference = payerReference,
            notificationDisabled = true
        };
        using var createResponse = await SendGatewayAsync(
            () => CreateJsonRequest(HttpMethod.Post, BuildUri(context, "customers"), createPayload),
            context,
            cancellationToken).ConfigureAwait(false);
        await EnsurePixSuccessAsync(createResponse, PixPaymentOperation.Customer, null, cancellationToken)
            .ConfigureAwait(false);
        using var createDocument = await ReadJsonAsync(createResponse, cancellationToken).ConfigureAwait(false);
        return GetRequiredString(createDocument.RootElement, "id");
    }

    private async Task<PixPaymentResult?> FindPixPaymentByReferenceAsync(
        Guid paymentId,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        var reference = PixExternalReference(paymentId);
        var query = $"payments?externalReference={Uri.EscapeDataString(reference)}&limit=2";
        using var response = await SendGatewayAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BuildUri(context, query)),
            context,
            cancellationToken).ConfigureAwait(false);
        await EnsurePixSuccessAsync(response, PixPaymentOperation.Query, null, cancellationToken)
            .ConfigureAwait(false);
        using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var matches = GetDataArray(document.RootElement)
            .Where(value => string.Equals(GetString(value, "externalReference"), reference, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length > 1)
        {
            throw new PixPaymentGatewayException(
                PixPaymentErrorCategory.SecurityBlock,
                "asaas_duplicate_pix_reference",
                "Multiple Asaas payments match the Pix external reference.");
        }

        if (matches.Length == 0)
            return null;

        var payment = ParsePixPayment(matches[0]).Result;
        return await AddPixPresentationAsync(payment, context, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PixPaymentResult?> TryFindPixPaymentAfterAmbiguousCreateAsync(
        Guid paymentId,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return await FindPixPaymentByReferenceAsync(paymentId, context, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is PixPaymentGatewayException or AsaasGatewayException)
        {
            return null;
        }
    }

    private async Task<PixPaymentResult> AddPixPresentationAsync(
        PixPaymentResult result,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        if (result.Status is not (PixPaymentStatus.AwaitingPayment or PixPaymentStatus.Processing))
            return result;

        using var response = await SendGatewayAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                BuildUri(context, $"payments/{Uri.EscapeDataString(result.ChargeId)}/pixQrCode")),
            context,
            cancellationToken).ConfigureAwait(false);
        await EnsurePixSuccessAsync(
            response,
            PixPaymentOperation.Presentation,
            result.ChargeId,
            cancellationToken).ConfigureAwait(false);
        using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var copyAndPaste = GetString(root, "payload")?.Trim();
        if (string.IsNullOrWhiteSpace(copyAndPaste)
            || copyAndPaste.Length is < 32 or > 8192
            || copyAndPaste.Any(char.IsControl))
        {
            throw new PixPaymentGatewayException(
                PixPaymentErrorCategory.AmbiguousResult,
                "asaas_invalid_pix_payload",
                "Asaas returned an invalid Pix payment payload.",
                providerChargeId: result.ChargeId);
        }

        result.CopyAndPaste = copyAndPaste;
        result.QrCodeImageDataUri = ParsePixImage(GetString(root, "encodedImage"), result.ChargeId);
        result.ExpiresAt = ParsePixDate(GetString(root, "expirationDate"));
        return result;
    }

    private static (PixPaymentResult Result, string? ExternalReference) ParsePixPayment(
        JsonElement element,
        string? fallbackChargeId = null)
    {
        var chargeId = GetString(element, "id")?.Trim() ?? fallbackChargeId;
        if (string.IsNullOrWhiteSpace(chargeId)
            || chargeId.Length > 200
            || chargeId.Any(char.IsControl))
        {
            throw new PixPaymentGatewayException(
                PixPaymentErrorCategory.AmbiguousResult,
                "asaas_missing_pix_payment_id",
                "Asaas accepted the Pix request but did not return a valid identifier.");
        }

        var providerStatus = GetString(element, "status")?.Trim().ToUpperInvariant() ?? "UNKNOWN";
        return (new PixPaymentResult
        {
            ProviderCode = ProviderCodeValue,
            ChargeId = chargeId,
            ProviderStatus = providerStatus,
            Status = MapPixStatus(providerStatus)
        }, NormalizeOptional(GetString(element, "externalReference")));
    }

    private static string ParsePixImage(string? encodedImage, string chargeId)
    {
        const string prefix = "data:image/png;base64,";
        var base64 = encodedImage?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true
            ? encodedImage[prefix.Length..]
            : encodedImage;
        if (string.IsNullOrWhiteSpace(base64) || base64.Length > 2_000_000)
            throw InvalidPixImage(chargeId);

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw InvalidPixImage(chargeId);
        }

        ReadOnlySpan<byte> pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < pngSignature.Length || !bytes.AsSpan(0, pngSignature.Length).SequenceEqual(pngSignature))
            throw InvalidPixImage(chargeId);
        return $"{prefix}{base64}";
    }

    private static PixPaymentGatewayException InvalidPixImage(string chargeId)
        => new(
            PixPaymentErrorCategory.AmbiguousResult,
            "asaas_invalid_pix_image",
            "Asaas returned an invalid Pix QR Code image.",
            providerChargeId: chargeId);

    private static void ValidatePixRequest(PixPaymentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.PaymentId == Guid.Empty || request.ContextId == Guid.Empty)
            throw new ArgumentException("Pix payment and context identifiers are required.", nameof(request));
        if (request.Value <= 0 || decimal.Round(request.Value, 2) != request.Value)
            throw new ArgumentException("A positive Pix payment value with two decimal places is required.", nameof(request));
        if (request.DueDate == default)
            throw new ArgumentException("A Pix payment due date is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Description)
            || request.Description.Trim().Length > 500
            || request.Description.Any(char.IsControl))
        {
            throw new ArgumentException("A valid Pix payment description is required.", nameof(request));
        }

        ArgumentNullException.ThrowIfNull(request.Payer);
        if (!string.IsNullOrWhiteSpace(request.Payer.ProviderCustomerId))
        {
            ValidatePixProviderCustomerId(request.Payer.ProviderCustomerId.Trim());
            return;
        }

        var document = OnlyDigits(request.Payer.Document);
        if (document.Length is not (11 or 14)
            || string.IsNullOrWhiteSpace(request.Payer.Name)
            || request.Payer.Name.Trim().Length > 100
            || request.Payer.Name.Any(char.IsControl))
        {
            throw new ArgumentException("A valid Pix payer identity or mapped customer is required.", nameof(request));
        }
    }

    private static void ValidatePixProviderCustomerId(string value)
    {
        if (value.Length > 100
            || !value.StartsWith("cus_", StringComparison.Ordinal)
            || value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_')))
        {
            throw new ArgumentException("The mapped Pix customer identifier is invalid.", nameof(value));
        }
    }

    private static void ValidatePixProviderChargeId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 200
            || value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
        {
            throw new ArgumentException("The Pix provider charge identifier is invalid.", nameof(value));
        }
    }

    private static void ValidatePixReference(string? actual, string expected)
    {
        if (actual is not null && !string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new PixPaymentGatewayException(
                PixPaymentErrorCategory.SecurityBlock,
                "asaas_pix_reference_mismatch",
                "Asaas returned inconsistent Pix payment data.");
        }
    }

    private async Task EnsurePixSuccessAsync(
        HttpResponseMessage response,
        PixPaymentOperation operation,
        string? providerChargeId,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var errorCode = await ReadErrorCodeAsync(response, cancellationToken).ConfigureAwait(false);
        var statusCode = (int)response.StatusCode;
        var category = response.StatusCode switch
        {
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => PixPaymentErrorCategory.Validation,
            HttpStatusCode.Unauthorized => PixPaymentErrorCategory.DefinitiveRejection,
            HttpStatusCode.Forbidden or HttpStatusCode.Conflict => PixPaymentErrorCategory.SecurityBlock,
            HttpStatusCode.TooManyRequests => PixPaymentErrorCategory.Retryable,
            _ when statusCode >= 500 && operation == PixPaymentOperation.Create
                => PixPaymentErrorCategory.AmbiguousResult,
            _ when statusCode >= 500 => PixPaymentErrorCategory.ProviderUnavailable,
            _ when operation == PixPaymentOperation.Create => PixPaymentErrorCategory.AmbiguousResult,
            _ => PixPaymentErrorCategory.DefinitiveRejection
        };
        throw new PixPaymentGatewayException(
            category,
            errorCode ?? $"asaas_http_{statusCode}",
            "The payment service rejected the Pix operation.",
            statusCode,
            providerChargeId);
    }

    private static PixPaymentGatewayException MapPixTransportException(
        PixPaymentOperation operation,
        string? providerChargeId,
        AsaasGatewayException exception)
        => new(
            operation == PixPaymentOperation.Create
                ? PixPaymentErrorCategory.AmbiguousResult
                : PixPaymentErrorCategory.ProviderUnavailable,
            exception.ErrorCode,
            "The payment service could not complete the Pix operation.",
            exception.HttpStatusCode,
            providerChargeId,
            exception);

    private static string PixExternalReference(Guid paymentId) => $"checkout:{paymentId:N}";

    private static string PixCustomerReference(Guid contextId) => $"checkout-customer:{contextId:N}";

    private static DateTimeOffset? ParsePixDate(string? value)
        => DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
                ? parsed
                : null;

    private static PixPaymentStatus MapPixStatus(string providerStatus)
        => providerStatus.ToUpperInvariant() switch
        {
            "PENDING" or "OVERDUE" => PixPaymentStatus.AwaitingPayment,
            "AWAITING_RISK_ANALYSIS" => PixPaymentStatus.Processing,
            "RECEIVED" or "CONFIRMED" or "RECEIVED_IN_CASH" => PixPaymentStatus.Paid,
            "DELETED" => PixPaymentStatus.Canceled,
            "REFUNDED" or "PARTIALLY_REFUNDED" or "REFUND_REQUESTED" => PixPaymentStatus.Refunded,
            _ => PixPaymentStatus.Unknown
        };

    private static string EventPixStatus(string eventType)
        => eventType switch
        {
            "PAYMENT_CONFIRMED" => "CONFIRMED",
            "PAYMENT_RECEIVED" => "RECEIVED",
            "PAYMENT_OVERDUE" => "OVERDUE",
            "PAYMENT_DELETED" or "PAYMENT_BANK_SLIP_CANCELLED" => "DELETED",
            "PAYMENT_REFUNDED" or "PAYMENT_PARTIALLY_REFUNDED" => "REFUNDED",
            _ => "PENDING"
        };

    private enum PixPaymentOperation : byte
    {
        Customer = 0,
        Create = 1,
        Query = 2,
        Presentation = 3
    }
}
