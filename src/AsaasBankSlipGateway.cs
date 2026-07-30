using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sufficit.Finance;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sufficit.Gateway.Asaas;

/// <summary>
/// Implements Asaas customer and bank slip operations using the official HTTP API.
/// </summary>
public sealed class AsaasBankSlipGateway : IBankSlipGateway, IBankSlipProviderDiagnosticsGateway
{
    public const string HttpClientName = "Sufficit.BankSlips.Asaas";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBankSlipCredentialResolver _credentialResolver;
    private readonly IOptionsMonitor<AsaasBankSlipGatewayOptions> _options;
    private readonly ILogger<AsaasBankSlipGateway> _logger;

    public AsaasBankSlipGateway(
        IHttpClientFactory httpClientFactory,
        IBankSlipCredentialResolver credentialResolver,
        IOptionsMonitor<AsaasBankSlipGatewayOptions> options,
        ILogger<AsaasBankSlipGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _credentialResolver = credentialResolver;
        _options = options;
        _logger = logger;
    }

    public string ProviderCode => BankSlipProviderCodes.Asaas;

    public async Task<BankSlipProviderDiagnosticGatewayResult?> ExecuteDiagnosticAsync(
        BankSlipProviderDiagnosticParameters parameters,
        BankSlipGatewayContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var relativePath = parameters.Operation switch
        {
            BankSlipProviderDiagnosticOperation.Authentication => "myAccount/commercialInfo/",
            BankSlipProviderDiagnosticOperation.Charge
                => $"payments/{Uri.EscapeDataString(GetRequiredProviderChargeId(parameters))}",
            _ => throw new ArgumentOutOfRangeException(
                nameof(parameters),
                parameters.Operation,
                "Unsupported Asaas diagnostic operation.")
        };

        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BuildUri(context, relativePath)),
            context,
            BankSlipOperation.Query,
            parameters.ProviderChargeId,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        return new BankSlipProviderDiagnosticGatewayResult
        {
            HttpStatusCode = (int)response.StatusCode,
            Payload = document.RootElement.Clone()
        };
    }

    public async Task<ProviderBankSlipResult> CreateAsync(
        BankSlipGatewayIssueRequest request,
        BankSlipGatewayContext context,
        CancellationToken cancellationToken)
    {
        ValidateIssueRequest(request);

        var existing = await FindPaymentByReferenceAsync(
            request.BankSlipId,
            context,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var customerId = await GetOrCreateCustomerAsync(request, context, cancellationToken).ConfigureAwait(false);
        var payload = new
        {
            customer = customerId,
            billingType = "BOLETO",
            value = request.Value,
            dueDate = request.Expiration.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            description = string.IsNullOrWhiteSpace(request.Description) ? "Serviços Sufficit" : request.Description,
            externalReference = request.BankSlipId.ToString("N"),
            postalService = false
        };

        try
        {
            using var response = await SendAsync(
                () => CreateJsonRequest(HttpMethod.Post, BuildUri(context, "payments"), payload),
                context,
                BankSlipOperation.Issue,
                null,
                cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, BankSlipOperation.Issue, null, cancellationToken).ConfigureAwait(false);
            using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            var result = ParsePayment(document.RootElement);
            return await EnrichBankSlipDataAsync(result, context, cancellationToken).ConfigureAwait(false);
        }
        catch (BankSlipGatewayException exception) when (exception.Category == BankSlipErrorCategory.AmbiguousResult)
        {
            _logger.LogWarning(
                "Asaas returned an ambiguous create result for bank slip {BankSlipId}; reconciling by external reference.",
                request.BankSlipId);

            var reconciled = await TryFindPaymentAfterAmbiguousCreateAsync(
                request.BankSlipId,
                context,
                cancellationToken).ConfigureAwait(false);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw;
        }
    }

    public async Task<ProviderBankSlipResult?> GetAsync(
        string providerChargeId,
        BankSlipGatewayContext context,
        CancellationToken cancellationToken)
    {
        ValidateProviderChargeId(providerChargeId);
        using var response = await SendAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                BuildUri(context, $"payments/{Uri.EscapeDataString(providerChargeId)}")),
            context,
            BankSlipOperation.Query,
            providerChargeId,
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, BankSlipOperation.Query, providerChargeId, cancellationToken).ConfigureAwait(false);
        using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var result = ParsePayment(document.RootElement, providerChargeId);
        return await EnrichBankSlipDataAsync(result, context, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProviderBankSlipCancellationResult> CancelAsync(
        string providerChargeId,
        BankSlipGatewayContext context,
        CancellationToken cancellationToken)
    {
        ValidateProviderChargeId(providerChargeId);
        var current = await GetAsync(providerChargeId, context, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            throw new BankSlipGatewayException(
                BankSlipErrorCategory.DefinitiveRejection,
                "asaas_payment_not_found",
                "The Asaas payment was not found.",
                (int)HttpStatusCode.NotFound,
                providerChargeId);
        }

        if (current.Status == BankSlipStatus.Paid)
        {
            throw new BankSlipGatewayException(
                BankSlipErrorCategory.DefinitiveRejection,
                "asaas_paid_payment_cannot_be_deleted",
                "A paid Asaas payment cannot be deleted.",
                providerChargeId: providerChargeId);
        }

        using var response = await SendAsync(
            () => new HttpRequestMessage(
                HttpMethod.Delete,
                BuildUri(context, $"payments/{Uri.EscapeDataString(providerChargeId)}")),
            context,
            BankSlipOperation.Cancel,
            providerChargeId,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, BankSlipOperation.Cancel, providerChargeId, cancellationToken).ConfigureAwait(false);
        using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var deleted = GetBoolean(document.RootElement, "deleted") == true;

        return new ProviderBankSlipCancellationResult
        {
            ProviderCode = ProviderCode,
            ChargeId = providerChargeId,
            ProviderStatus = deleted ? "deleted" : "unknown",
            Canceled = deleted
        };
    }

    private async Task<string> GetOrCreateCustomerAsync(
        BankSlipGatewayIssueRequest request,
        BankSlipGatewayContext context,
        CancellationToken cancellationToken)
    {
        var document = OnlyDigits(request.Payer.Document);
        var externalReference = request.ContextId.ToString("N");
        var query = $"customers?cpfCnpj={Uri.EscapeDataString(document)}&externalReference={Uri.EscapeDataString(externalReference)}&limit=2";
        using (var searchResponse = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BuildUri(context, query)),
            context,
            BankSlipOperation.Query,
            null,
            cancellationToken).ConfigureAwait(false))
        {
            await EnsureSuccessAsync(searchResponse, BankSlipOperation.Query, null, cancellationToken).ConfigureAwait(false);
            using var searchDocument = await ReadJsonAsync(searchResponse, cancellationToken).ConfigureAwait(false);
            var matches = GetDataArray(searchDocument.RootElement)
                .Where(value => string.Equals(GetString(value, "cpfCnpj"), document, StringComparison.Ordinal)
                    && string.Equals(GetString(value, "externalReference"), externalReference, StringComparison.Ordinal))
                .ToArray();

            if (matches.Length > 1)
            {
                throw new BankSlipGatewayException(
                    BankSlipErrorCategory.SecurityBlock,
                    "asaas_duplicate_customers",
                    "Multiple Asaas customers match the tenant context and payer document.");
            }

            if (matches.Length == 1)
            {
                return GetRequiredString(matches[0], "id");
            }
        }

        var payer = request.Payer;
        var payload = new
        {
            name = payer.Name,
            cpfCnpj = document,
            email = payer.Email,
            mobilePhone = OnlyDigits(payer.Phone),
            address = payer.Address?.Street,
            addressNumber = payer.Address?.Number,
            complement = payer.Address?.Complement,
            province = payer.Address?.Neighborhood,
            postalCode = OnlyDigits(payer.Address?.PostalCode),
            externalReference,
            notificationDisabled = true
        };

        using var createResponse = await SendAsync(
            () => CreateJsonRequest(HttpMethod.Post, BuildUri(context, "customers"), payload),
            context,
            BankSlipOperation.Query,
            null,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(createResponse, BankSlipOperation.Query, null, cancellationToken).ConfigureAwait(false);
        using var createDocument = await ReadJsonAsync(createResponse, cancellationToken).ConfigureAwait(false);
        return GetRequiredString(createDocument.RootElement, "id");
    }

    private async Task<ProviderBankSlipResult?> FindPaymentByReferenceAsync(
        Guid bankSlipId,
        BankSlipGatewayContext context,
        CancellationToken cancellationToken)
    {
        var reference = bankSlipId.ToString("N");
        var query = $"payments?externalReference={Uri.EscapeDataString(reference)}&limit=2";
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BuildUri(context, query)),
            context,
            BankSlipOperation.Query,
            null,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, BankSlipOperation.Query, null, cancellationToken).ConfigureAwait(false);
        using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var matches = GetDataArray(document.RootElement)
            .Where(value => string.Equals(GetString(value, "externalReference"), reference, StringComparison.Ordinal))
            .ToArray();

        if (matches.Length > 1)
        {
            throw new BankSlipGatewayException(
                BankSlipErrorCategory.SecurityBlock,
                "asaas_duplicate_external_reference",
                "Multiple Asaas payments match the bank slip external reference.");
        }

        if (matches.Length == 0)
        {
            return null;
        }

        var result = ParsePayment(matches[0]);
        return await EnrichBankSlipDataAsync(result, context, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProviderBankSlipResult?> TryFindPaymentAfterAmbiguousCreateAsync(
        Guid bankSlipId,
        BankSlipGatewayContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return await FindPaymentByReferenceAsync(bankSlipId, context, cancellationToken).ConfigureAwait(false);
        }
        catch (BankSlipGatewayException)
        {
            return null;
        }
    }

    private async Task<ProviderBankSlipResult> EnrichBankSlipDataAsync(
        ProviderBankSlipResult result,
        BankSlipGatewayContext context,
        CancellationToken cancellationToken)
    {
        if (result.Status != BankSlipStatus.Ready && result.Status != BankSlipStatus.Processing)
        {
            return result;
        }

        using var response = await SendAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                BuildUri(context, $"payments/{Uri.EscapeDataString(result.ChargeId)}/identificationField")),
            context,
            BankSlipOperation.Query,
            result.ChargeId,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, BankSlipOperation.Query, result.ChargeId, cancellationToken).ConfigureAwait(false);
        using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        result.BarCode = GetString(document.RootElement, "identificationField")
            ?? GetString(document.RootElement, "barCode");
        return result;
    }

    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> requestFactory,
        BankSlipGatewayContext context,
        BankSlipOperation operation,
        string? providerChargeId,
        CancellationToken cancellationToken)
    {
        var credential = await _credentialResolver
            .GetRequiredAsync(ProviderCode, context, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(credential.ApiKey))
        {
            throw new BankSlipGatewayException(
                BankSlipErrorCategory.DefinitiveRejection,
                "asaas_credentials_missing",
                "Asaas credentials are not configured for the selected tenant.");
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = _options.CurrentValue.Timeout;
        using var request = requestFactory();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("access_token", credential.ApiKey);
        request.Headers.TryAddWithoutValidation("User-Agent", _options.CurrentValue.UserAgent);

        try
        {
            return await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw CreateTransportException(operation, "asaas_timeout", providerChargeId, exception);
        }
        catch (HttpRequestException exception)
        {
            throw CreateTransportException(operation, "asaas_transport_error", providerChargeId, exception);
        }
    }

    private Uri BuildUri(BankSlipGatewayContext context, string relativePath)
    {
        var options = _options.CurrentValue;
        var baseAddress = context.Environment == BankSlipProviderEnvironment.Production
            ? options.ProductionBaseAddress
            : options.SandboxBaseAddress;
        return new Uri(baseAddress, relativePath);
    }

    private static HttpRequestMessage CreateJsonRequest(HttpMethod method, Uri uri, object payload)
        => new(method, uri)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };

    private static ProviderBankSlipResult ParsePayment(JsonElement element, string? fallbackChargeId = null)
    {
        var chargeId = GetString(element, "id") ?? fallbackChargeId;
        if (string.IsNullOrWhiteSpace(chargeId))
        {
            throw new BankSlipGatewayException(
                BankSlipErrorCategory.AmbiguousResult,
                "asaas_missing_payment_id",
                "Asaas accepted the payment request but did not return an identifier.");
        }

        var providerStatus = GetString(element, "status") ?? "UNKNOWN";
        var urlValue = GetString(element, "bankSlipUrl") ?? GetString(element, "invoiceUrl");
        var customerId = GetString(element, "customer");

        return new ProviderBankSlipResult
        {
            ProviderCode = BankSlipProviderCodes.Asaas,
            ChargeId = chargeId,
            ProviderStatus = providerStatus,
            Status = MapStatus(providerStatus),
            Url = Uri.TryCreate(urlValue, UriKind.Absolute, out var url) ? url : null,
            Attributes = string.IsNullOrWhiteSpace(customerId)
                ? null
                : new Dictionary<string, string> { ["asaas.customer_id"] = customerId }
        };
    }

    private static BankSlipStatus MapStatus(string providerStatus)
        => providerStatus.ToUpperInvariant() switch
        {
            "PENDING" => BankSlipStatus.Ready,
            "OVERDUE" => BankSlipStatus.Ready,
            "DUNNING_REQUESTED" => BankSlipStatus.Ready,
            "DUNNING_RECEIVED" => BankSlipStatus.Ready,
            "AWAITING_RISK_ANALYSIS" => BankSlipStatus.Processing,
            "RECEIVED" => BankSlipStatus.Paid,
            "CONFIRMED" => BankSlipStatus.Paid,
            "RECEIVED_IN_CASH" => BankSlipStatus.Paid,
            "DELETED" => BankSlipStatus.Canceled,
            _ => BankSlipStatus.ReconciliationPending
        };

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        BankSlipOperation operation,
        string? providerChargeId,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errorCode = await ReadErrorCodeAsync(response, cancellationToken).ConfigureAwait(false);
        var statusCode = (int)response.StatusCode;
        var category = response.StatusCode switch
        {
            HttpStatusCode.BadRequest => BankSlipErrorCategory.Validation,
            HttpStatusCode.Unauthorized => BankSlipErrorCategory.DefinitiveRejection,
            HttpStatusCode.Forbidden => BankSlipErrorCategory.SecurityBlock,
            HttpStatusCode.Conflict => BankSlipErrorCategory.SecurityBlock,
            HttpStatusCode.UnprocessableEntity => BankSlipErrorCategory.Validation,
            HttpStatusCode.TooManyRequests => BankSlipErrorCategory.Retryable,
            _ when statusCode >= 500 && operation == BankSlipOperation.Issue => BankSlipErrorCategory.AmbiguousResult,
            _ when statusCode >= 500 => BankSlipErrorCategory.ProviderUnavailable,
            _ when operation == BankSlipOperation.Issue => BankSlipErrorCategory.AmbiguousResult,
            _ => BankSlipErrorCategory.DefinitiveRejection
        };

        throw new BankSlipGatewayException(
            category,
            errorCode ?? $"asaas_http_{statusCode}",
            $"Asaas rejected the {operation.ToString().ToLowerInvariant()} operation.",
            statusCode,
            providerChargeId);
    }

    private static BankSlipGatewayException CreateTransportException(
        BankSlipOperation operation,
        string errorCode,
        string? providerChargeId,
        Exception exception)
        => new(
            operation == BankSlipOperation.Issue
                ? BankSlipErrorCategory.AmbiguousResult
                : BankSlipErrorCategory.ProviderUnavailable,
            errorCode,
            $"Asaas {operation.ToString().ToLowerInvariant()} transport failed.",
            providerChargeId: providerChargeId,
            innerException: exception);

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> ReadErrorCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            if (document.RootElement.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array
                && errors.GetArrayLength() > 0)
            {
                return GetString(errors[0], "code");
            }

            return GetString(document.RootElement, "code");
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static JsonElement[] GetDataArray(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<JsonElement>();
        }

        return data.EnumerateArray().ToArray();
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
        => GetString(element, propertyName)
            ?? throw new BankSlipGatewayException(
                BankSlipErrorCategory.AmbiguousResult,
                $"asaas_missing_{propertyName}",
                $"Asaas did not return the required '{propertyName}' property.");

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }

    private static bool? GetBoolean(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False)
                ? property.GetBoolean()
                : null;

    private static string OnlyDigits(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsDigit).ToArray());

    private static void ValidateIssueRequest(BankSlipGatewayIssueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Payer);

        if (request.BankSlipId == Guid.Empty || request.ContextId == Guid.Empty)
        {
            throw new ArgumentException("Bank slip and context identifiers are required.", nameof(request));
        }

        if (request.Value <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Bank slip value must be positive.");
        }

        var documentLength = OnlyDigits(request.Payer.Document).Length;
        if (documentLength != 11 && documentLength != 14)
        {
            throw new ArgumentException("Payer document must be a CPF or CNPJ.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Payer.Name))
        {
            throw new ArgumentException("Payer name is required.", nameof(request));
        }
    }

    private static void ValidateProviderChargeId(string providerChargeId)
    {
        if (string.IsNullOrWhiteSpace(providerChargeId))
        {
            throw new ArgumentException("Provider charge identifier is required.", nameof(providerChargeId));
        }
    }

    private static string GetRequiredProviderChargeId(
        BankSlipProviderDiagnosticParameters parameters)
    {
        ValidateProviderChargeId(parameters.ProviderChargeId!);
        return parameters.ProviderChargeId!;
    }
}
