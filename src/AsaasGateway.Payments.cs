using Sufficit.Gateway;
using System.Globalization;
using System.Net;

namespace Sufficit.Gateway.Asaas;

/// <summary>
/// Native Asaas payments API (/v3/payments). Speaks only Asaas vocabulary;
/// bank slip and Pix domain translation stays in the Finance partials.
/// </summary>
public sealed partial class AsaasGateway : IAsaasPaymentGateway
{
    public async Task<AsaasPayment> CreatePaymentAsync(
        AsaasPaymentCreateRequest request,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateContext(context);
        ValidatePaymentCreateRequest(request);

        using var response = await SendGatewayAsync(
            () => CreateJsonRequest(HttpMethod.Post, BuildUri(context, "payments"), request),
            context,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "payment", cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<AsaasPayment>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AsaasPayment?> GetPaymentAsync(
        string paymentId,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ValidatePaymentId(paymentId);
        ValidateContext(context);

        using var response = await SendGatewayAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                BuildUri(context, $"payments/{Uri.EscapeDataString(paymentId.Trim())}")),
            context,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "payment", cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<AsaasPayment>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AsaasPaymentPage> ListPaymentsAsync(
        AsaasPaymentSearchParameters parameters,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ValidateContext(context);
        ValidatePageArguments(parameters.Offset, parameters.Limit);

        var query = BuildPaymentQuery(parameters);
        using var response = await SendGatewayAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BuildUri(context, query)),
            context,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "payment", cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<AsaasPaymentPage>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AsaasPaymentStatus?> GetPaymentStatusAsync(
        string paymentId,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ValidatePaymentId(paymentId);
        ValidateContext(context);

        using var response = await SendGatewayAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                BuildUri(context, $"payments/{Uri.EscapeDataString(paymentId.Trim())}/status")),
            context,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "payment status", cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<AsaasPaymentStatus>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AsaasIdentificationField?> GetIdentificationFieldAsync(
        string paymentId,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ValidatePaymentId(paymentId);
        ValidateContext(context);

        using var response = await SendGatewayAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                BuildUri(context, $"payments/{Uri.EscapeDataString(paymentId.Trim())}/identificationField")),
            context,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "payment identification field", cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<AsaasIdentificationField>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AsaasPixQrCode?> GetPixQrCodeAsync(
        string paymentId,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ValidatePaymentId(paymentId);
        ValidateContext(context);

        using var response = await SendGatewayAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                BuildUri(context, $"payments/{Uri.EscapeDataString(paymentId.Trim())}/pixQrCode")),
            context,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "payment Pix QR Code", cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<AsaasPixQrCode>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AsaasDeletionResult> DeletePaymentAsync(
        string paymentId,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ValidatePaymentId(paymentId);
        ValidateContext(context);

        using var response = await SendGatewayAsync(
            () => new HttpRequestMessage(
                HttpMethod.Delete,
                BuildUri(context, $"payments/{Uri.EscapeDataString(paymentId.Trim())}")),
            context,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "payment deletion", cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<AsaasDeletionResult>(response, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildPaymentQuery(AsaasPaymentSearchParameters parameters)
    {
        var values = new List<KeyValuePair<string, string>>
        {
            new("offset", parameters.Offset.ToString(CultureInfo.InvariantCulture)),
            new("limit", parameters.Limit.ToString(CultureInfo.InvariantCulture))
        };
        AddQueryValue(values, "customer", parameters.CustomerId);
        AddQueryValue(values, "externalReference", parameters.ExternalReference);
        AddQueryValue(values, "status", parameters.Status);
        AddQueryValue(values, "billingType", parameters.BillingType);
        return "payments?" + string.Join(
            "&",
            values.Select(value =>
                $"{Uri.EscapeDataString(value.Key)}={Uri.EscapeDataString(value.Value)}"));
    }

    private static void ValidatePaymentCreateRequest(AsaasPaymentCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerId))
        {
            throw new ArgumentException("An Asaas customer identifier is required.", nameof(request));
        }

        if (!AsaasPaymentBillingTypes.IsKnown(request.BillingType))
        {
            throw new ArgumentException(
                $"A known Asaas billing type is required ({AsaasPaymentBillingTypes.Boleto}, "
                + $"{AsaasPaymentBillingTypes.Pix}, {AsaasPaymentBillingTypes.CreditCard}).",
                nameof(request));
        }

        if (request.Value <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Payment value must be positive.");
        }

        if (request.DueDate == default)
        {
            throw new ArgumentException("A payment due date is required.", nameof(request));
        }

        if (request.InstallmentCount is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Installment count must be at least one.");
        }

        if (request.InstallmentCount > 1
            && (request.InstallmentValue is null || request.InstallmentValue <= 0m))
        {
            throw new ArgumentException(
                "A positive installment value is required for installment payments.",
                nameof(request));
        }
    }

    private static void ValidatePaymentId(string paymentId)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            throw new ArgumentException("An Asaas payment identifier is required.", nameof(paymentId));
        }
    }

    private static void ValidatePageArguments(int offset, int limit)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative.");
        }

        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 100.");
        }
    }
}
