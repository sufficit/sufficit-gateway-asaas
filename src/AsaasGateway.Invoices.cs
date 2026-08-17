using Sufficit.Gateway;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Sufficit.Gateway.Asaas;

public sealed partial class AsaasGateway : IAsaasInvoiceGateway
{
    public async Task<AsaasCustomer?> GetCustomerAsync(
        string customerId,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            throw new ArgumentException("A customer id is required.", nameof(customerId));
        ValidateContext(context);

        using var response = await SendGatewayAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                BuildUri(context, $"customers/{Uri.EscapeDataString(customerId.Trim())}")),
            context,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        await EnsureInvoiceSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<AsaasCustomer>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AsaasInvoiceDocument> DownloadDocumentAsync(
        Uri documentUrl,
        CancellationToken cancellationToken)
    {
        ValidateInvoiceDocumentUrl(documentUrl);
        var options = _options.CurrentValue;
        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = options.Timeout;
        using var request = new HttpRequestMessage(HttpMethod.Get, documentUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", options.UserAgent);
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AsaasGatewayException(
                $"asaas_invoice_document_http_{(int)response.StatusCode}",
                "The invoice document could not be downloaded.",
                (int)response.StatusCode,
                retryAfter: ReadRetryAfter(response));
        }

        var maximumBytes = Math.Max(1024, options.MaxInvoiceDocumentBytes);
        if (response.Content.Headers.ContentLength is > 0
            && response.Content.Headers.ContentLength > maximumBytes)
        {
            throw new AsaasGatewayException(
                "asaas_invoice_document_too_large",
                "The invoice document exceeds the configured size limit.");
        }

        await using var source = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            if (destination.Length + read > maximumBytes)
            {
                throw new AsaasGatewayException(
                    "asaas_invoice_document_too_large",
                    "The invoice document exceeds the configured size limit.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }

        return new AsaasInvoiceDocument
        {
            Content = destination.ToArray(),
            ContentType = response.Content.Headers.ContentType?.MediaType
                ?? "application/octet-stream"
        };
    }

    public async Task<AsaasInvoicePage> ListInvoicesAsync(
        AsaasInvoiceSearchParameters parameters,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ValidateContext(context);

        if (parameters.Offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), "Offset cannot be negative.");
        }

        if (parameters.Limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), "Limit must be between 1 and 100.");
        }

        var query = BuildInvoiceQuery(parameters);
        using var response = await SendGatewayAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BuildUri(context, query)),
            context,
            cancellationToken).ConfigureAwait(false);
        await EnsureInvoiceSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<AsaasInvoicePage>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AsaasInvoice?> GetInvoiceAsync(
        string invoiceId,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ValidateInvoiceId(invoiceId);
        ValidateContext(context);

        using var response = await SendGatewayAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                BuildUri(context, $"invoices/{Uri.EscapeDataString(invoiceId)}")),
            context,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureInvoiceSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<AsaasInvoice>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AsaasInvoice> ScheduleInvoiceAsync(
        AsaasInvoiceScheduleRequest request,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ValidateScheduleRequest(request);
        ValidateContext(context);

        using var response = await SendGatewayAsync(
            () => CreateJsonRequest(HttpMethod.Post, BuildUri(context, "invoices"), request),
            context,
            cancellationToken).ConfigureAwait(false);
        await EnsureInvoiceSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<AsaasInvoice>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AsaasInvoice> UpdateInvoiceAsync(
        string invoiceId,
        AsaasInvoiceUpdateRequest request,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ValidateInvoiceId(invoiceId);
        ArgumentNullException.ThrowIfNull(request);
        ValidateContext(context);

        using var response = await SendGatewayAsync(
            () => CreateJsonRequest(
                HttpMethod.Put,
                BuildUri(context, $"invoices/{Uri.EscapeDataString(invoiceId)}"),
                request),
            context,
            cancellationToken).ConfigureAwait(false);
        await EnsureInvoiceSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<AsaasInvoice>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AsaasInvoice> AuthorizeInvoiceAsync(
        string invoiceId,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ValidateInvoiceId(invoiceId);
        ValidateContext(context);

        using var response = await SendGatewayAsync(
            () => new HttpRequestMessage(
                HttpMethod.Post,
                BuildUri(context, $"invoices/{Uri.EscapeDataString(invoiceId)}/authorize")),
            context,
            cancellationToken).ConfigureAwait(false);
        await EnsureInvoiceSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<AsaasInvoice>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AsaasInvoice> CancelInvoiceAsync(
        string invoiceId,
        AsaasInvoiceCancelRequest request,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ValidateInvoiceId(invoiceId);
        ArgumentNullException.ThrowIfNull(request);
        ValidateContext(context);

        using var response = await SendGatewayAsync(
            () => CreateJsonRequest(
                HttpMethod.Post,
                BuildUri(context, $"invoices/{Uri.EscapeDataString(invoiceId)}/cancel"),
                request),
            context,
            cancellationToken).ConfigureAwait(false);
        await EnsureInvoiceSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<AsaasInvoice>(response, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildInvoiceQuery(AsaasInvoiceSearchParameters parameters)
    {
        var values = new List<KeyValuePair<string, string>>
        {
            new("offset", parameters.Offset.ToString(CultureInfo.InvariantCulture)),
            new("limit", parameters.Limit.ToString(CultureInfo.InvariantCulture))
        };
        AddQueryValue(values, "effectiveDate[ge]", parameters.EffectiveDateFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AddQueryValue(values, "effectiveDate[le]", parameters.EffectiveDateTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AddQueryValue(values, "payment", parameters.PaymentId);
        AddQueryValue(values, "installment", parameters.InstallmentId);
        AddQueryValue(values, "customer", parameters.CustomerId);
        AddQueryValue(values, "externalReference", parameters.ExternalReference);
        AddQueryValue(values, "status", parameters.Status);
        return "invoices?" + string.Join(
            "&",
            values.Select(value =>
                $"{Uri.EscapeDataString(value.Key)}={Uri.EscapeDataString(value.Value)}"));
    }

    private void ValidateInvoiceDocumentUrl(Uri documentUrl)
    {
        ArgumentNullException.ThrowIfNull(documentUrl);
        if (!documentUrl.IsAbsoluteUri
            || !string.Equals(documentUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(documentUrl.UserInfo)
            || documentUrl.IsLoopback)
        {
            throw new ArgumentException("A public HTTPS invoice document URL is required.", nameof(documentUrl));
        }

        var allowed = _options.CurrentValue.InvoiceDocumentHosts
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().TrimStart('.'))
            .Any(value => string.Equals(documentUrl.Host, value, StringComparison.OrdinalIgnoreCase)
                || documentUrl.Host.EndsWith('.' + value, StringComparison.OrdinalIgnoreCase));
        if (!allowed)
            throw new ArgumentException("The invoice document host is not allowed.", nameof(documentUrl));
    }

    private static void AddQueryValue(
        ICollection<KeyValuePair<string, string>> values,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(new KeyValuePair<string, string>(key, value));
        }
    }

    private static async Task EnsureInvoiceSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var statusCode = (int)response.StatusCode;
        var errorCode = await ReadErrorCodeAsync(response, cancellationToken).ConfigureAwait(false)
            ?? $"asaas_http_{statusCode}";
        throw new AsaasGatewayException(
            errorCode,
            "Asaas rejected the invoice operation.",
            statusCode,
            retryAfter: ReadRetryAfter(response));
    }

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        return document.RootElement.Deserialize<T>(JsonOptions)
            ?? throw new AsaasGatewayException(
                "asaas_invalid_response",
                "Asaas returned an empty or invalid response.");
    }

    private static void ValidateScheduleRequest(AsaasInvoiceScheduleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sourceCount = new[]
        {
            request.PaymentId,
            request.InstallmentId,
            request.CustomerId
        }.Count(value => !string.IsNullOrWhiteSpace(value));
        if (sourceCount != 1)
        {
            throw new ArgumentException(
                "Exactly one of payment, installment or customer must identify the invoice source.",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ServiceDescription))
        {
            throw new ArgumentException("Service description is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Observations))
        {
            throw new ArgumentException("Invoice observations are required.", nameof(request));
        }

        if (request.Value <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Invoice value must be positive.");
        }

        if (request.Deductions < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Invoice deductions cannot be negative.");
        }

        if (request.EffectiveDate == default)
        {
            throw new ArgumentException("Effective date is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.MunicipalServiceId)
            && string.IsNullOrWhiteSpace(request.MunicipalServiceCode))
        {
            throw new ArgumentException(
                "Municipal service id or municipal service code is required.",
                nameof(request));
        }

        if (request.Taxes.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new ArgumentException("Invoice taxes are required.", nameof(request));
        }
    }

    private static void ValidateInvoiceId(string invoiceId)
    {
        if (string.IsNullOrWhiteSpace(invoiceId))
        {
            throw new ArgumentException("Asaas invoice identifier is required.", nameof(invoiceId));
        }
    }

    private static void ValidateContext(GatewayCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.TenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant identifier is required.", nameof(context));
        }

        if (string.IsNullOrWhiteSpace(context.CredentialReference))
        {
            throw new ArgumentException("Credential reference is required.", nameof(context));
        }
    }
}
