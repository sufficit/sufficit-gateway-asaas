using Sufficit.Gateway;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Sufficit.Gateway.Asaas;

public sealed partial class AsaasGateway : IAsaasCheckoutGateway
{
    private static readonly HashSet<string> CheckoutEvents = new(StringComparer.Ordinal)
    {
        "CHECKOUT_CREATED",
        "CHECKOUT_CANCELED",
        "CHECKOUT_EXPIRED",
        "CHECKOUT_PAID"
    };

    public async Task<AsaasCheckoutAccount> GetAccountAsync(
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        using var response = await SendGatewayAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                BuildUri(context, "myAccount/commercialInfo/")),
            context,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var providerCode = await ReadErrorCodeAsync(response, cancellationToken).ConfigureAwait(false);
            throw new AsaasGatewayException(
                providerCode ?? "asaas_account_verification_failed",
                response.StatusCode == HttpStatusCode.Unauthorized
                    ? "Asaas rejected the configured API credential."
                    : "The Asaas account identity could not be verified.",
                (int)response.StatusCode,
                retryAfter: ReadRetryAfter(response));
        }

        using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var accountDocument = DigitsOnly(GetString(root, "cpfCnpj"));
        var accountStatus = NormalizeOptional(GetString(root, "status"))?.ToUpperInvariant();
        var sandboxAccountWithoutDocument = context.Environment == GatewayEnvironment.Sandbox
            && accountDocument.Length == 0
            && string.Equals(accountStatus, "APPROVED", StringComparison.Ordinal);
        if (accountDocument.Length is not (11 or 14) && !sandboxAccountWithoutDocument)
        {
            throw new AsaasGatewayException(
                "asaas_account_invalid_response",
                "Asaas returned an invalid account identity.");
        }

        return new AsaasCheckoutAccount
        {
            Document = accountDocument,
            CompanyName = NormalizeOptional(GetString(root, "companyName")),
            PersonType = NormalizeOptional(GetString(root, "personType")),
            Status = accountStatus
        };
    }

    public async Task<AsaasCheckoutResult> CreateAsync(
        AsaasCheckoutRequest request,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ValidateCheckoutRequest(request);
        ArgumentNullException.ThrowIfNull(context);

        var payload = new
        {
            billingTypes = request.BillingTypes
                .Distinct()
                .Select(MapBillingType)
                .ToArray(),
            chargeTypes = new[] { "DETACHED" },
            minutesToExpire = request.MinutesToExpire,
            externalReference = request.ExternalReference.Trim(),
            callback = new
            {
                successUrl = request.SuccessUrl.AbsoluteUri,
                cancelUrl = request.CancelUrl.AbsoluteUri,
                expiredUrl = request.ExpiredUrl.AbsoluteUri
            },
            items = request.Items.Select(item => new
            {
                externalReference = NormalizeOptional(item.ExternalReference),
                name = item.Name.Trim(),
                description = NormalizeOptional(item.Description),
                quantity = item.Quantity,
                value = item.Value
            }).ToArray()
        };

        using var response = await SendGatewayAsync(
            () => CreateJsonRequest(HttpMethod.Post, BuildUri(context, "checkouts"), payload),
            context,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var providerCode = await ReadErrorCodeAsync(response, cancellationToken).ConfigureAwait(false);
            throw new AsaasGatewayException(
                providerCode ?? "asaas_checkout_create_failed",
                response.StatusCode == HttpStatusCode.Unauthorized
                    ? "Asaas rejected the configured API credential."
                    : "Asaas rejected the hosted checkout request.",
                (int)response.StatusCode,
                retryAfter: ReadRetryAfter(response));
        }

        using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var checkoutId = GetString(root, "id")?.Trim();
        if (string.IsNullOrWhiteSpace(checkoutId)
            || checkoutId.Length > 200
            || checkoutId.Any(char.IsControl))
        {
            throw new AsaasGatewayException(
                "asaas_checkout_invalid_response",
                "Asaas returned an invalid hosted checkout identifier.");
        }

        var status = GetString(root, "status")?.Trim().ToUpperInvariant() ?? "ACTIVE";
        var externalReference = NormalizeOptional(GetString(root, "externalReference"));
        if (!string.Equals(status, "ACTIVE", StringComparison.Ordinal)
            || (externalReference is not null
                && !string.Equals(
                    externalReference,
                    request.ExternalReference.Trim(),
                    StringComparison.Ordinal)))
        {
            throw new AsaasGatewayException(
                "asaas_checkout_invalid_response",
                "Asaas returned inconsistent hosted checkout data.");
        }

        var link = ParseCheckoutLink(GetString(root, "link"), checkoutId, context.Environment);
        return new AsaasCheckoutResult
        {
            Id = checkoutId,
            Link = link,
            Status = status,
            ExternalReference = externalReference
        };
    }

    public Task<bool> AuthenticateWebhookAsync(
        IReadOnlyDictionary<string, string> requestHeaders,
        GatewayCallContext context,
        CancellationToken cancellationToken)
        => AuthenticateWebhookCoreAsync(requestHeaders, context, cancellationToken);

    public AsaasCheckoutWebhookEvent ParseCheckoutWebhook(string requestPayload)
    {
        if (string.IsNullOrWhiteSpace(requestPayload))
            throw new FormatException("The Asaas checkout webhook payload is empty.");

        using var document = JsonDocument.Parse(requestPayload);
        var root = document.RootElement;
        var eventId = GetString(root, "id")?.Trim();
        var eventType = GetString(root, "event")?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(eventId)
            || eventId.Length > 255
            || string.IsNullOrWhiteSpace(eventType)
            || !CheckoutEvents.Contains(eventType))
        {
            throw new FormatException("The Asaas checkout webhook has an invalid event identity or type.");
        }

        if (!root.TryGetProperty("checkout", out var checkout)
            || checkout.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("The Asaas checkout webhook does not contain a checkout object.");
        }

        var checkoutId = GetString(checkout, "id")?.Trim();
        if (string.IsNullOrWhiteSpace(checkoutId)
            || checkoutId.Length > 200
            || checkoutId.Any(char.IsControl))
            throw new FormatException("The Asaas checkout webhook does not contain a valid checkout identifier.");

        return new AsaasCheckoutWebhookEvent
        {
            EventId = eventId,
            EventType = eventType,
            CreatedAt = ParseCheckoutDate(GetString(root, "dateCreated")),
            CheckoutId = checkoutId,
            Status = GetString(checkout, "status")?.Trim().ToUpperInvariant() ?? EventStatus(eventType),
            ExternalReference = NormalizeOptional(GetString(checkout, "externalReference"))
        };
    }

    private static void ValidateCheckoutRequest(AsaasCheckoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.BillingTypes.Count is < 1 or > 2
            || request.BillingTypes.Distinct().Count() != request.BillingTypes.Count)
        {
            throw new ArgumentException("One or two distinct checkout billing types are required.", nameof(request));
        }

        if (request.MinutesToExpire is < 10 or > 1440)
            throw new ArgumentOutOfRangeException(nameof(request), "Checkout expiration must be between 10 and 1440 minutes.");
        if (string.IsNullOrWhiteSpace(request.ExternalReference)
            || request.ExternalReference.Trim().Length > 200
            || request.ExternalReference.Any(char.IsControl))
        {
            throw new ArgumentException("A checkout external reference with at most 200 characters is required.", nameof(request));
        }

        ValidateCallbackUrl(request.SuccessUrl, nameof(request.SuccessUrl));
        ValidateCallbackUrl(request.CancelUrl, nameof(request.CancelUrl));
        ValidateCallbackUrl(request.ExpiredUrl, nameof(request.ExpiredUrl));

        if (request.Items.Count is < 1 or > 100)
            throw new ArgumentException("At least one checkout item is required.", nameof(request));
        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Trim().Length > 255 || item.Name.Any(char.IsControl))
                throw new ArgumentException("Every checkout item requires a valid name.", nameof(request));
            if (item.Quantity < 1 || item.Value <= 0 || decimal.Round(item.Value, 2) != item.Value)
                throw new ArgumentException("Every checkout item requires a positive quantity and monetary value.", nameof(request));
            if (!ValidOptionalText(item.ExternalReference, 200)
                || !ValidOptionalText(item.Description, 500))
                throw new ArgumentException("Checkout item text exceeds the supported length.", nameof(request));
        }
    }

    private static void ValidateCallbackUrl(Uri? value, string parameterName)
    {
        if (value is null
            || !value.IsAbsoluteUri
            || !string.Equals(value.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(value.UserInfo)
            || !string.IsNullOrEmpty(value.Fragment))
        {
            throw new ArgumentException("Checkout callbacks must use absolute HTTPS URLs.", parameterName);
        }
    }

    private static string MapBillingType(AsaasCheckoutBillingType value) => value switch
    {
        AsaasCheckoutBillingType.Pix => "PIX",
        AsaasCheckoutBillingType.CreditCard => "CREDIT_CARD",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static Uri ParseCheckoutLink(
        string? rawLink,
        string checkoutId,
        GatewayEnvironment environment)
    {
        if (Uri.TryCreate(rawLink, UriKind.Absolute, out var returned)
            && string.Equals(returned.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && returned.IsDefaultPort
            && string.IsNullOrEmpty(returned.UserInfo)
            && (string.Equals(returned.Host, "asaas.com", StringComparison.OrdinalIgnoreCase)
                || returned.Host.EndsWith(".asaas.com", StringComparison.OrdinalIgnoreCase)))
        {
            return returned;
        }

        var host = environment == GatewayEnvironment.Production ? "asaas.com" : "sandbox.asaas.com";
        return new Uri($"https://{host}/checkoutSession/show?id={Uri.EscapeDataString(checkoutId)}");
    }

    private static DateTimeOffset? ParseCheckoutDate(string? value)
        => DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
                ? parsed
                : null;

    private static string EventStatus(string eventType) => eventType switch
    {
        "CHECKOUT_PAID" => "PAID",
        "CHECKOUT_CANCELED" => "CANCELED",
        "CHECKOUT_EXPIRED" => "EXPIRED",
        _ => "ACTIVE"
    };

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool ValidOptionalText(string? value, int maximumLength)
        => value is null || (value.Length <= maximumLength && !value.Any(char.IsControl));

    private static string DigitsOnly(string? value)
        => new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
}
