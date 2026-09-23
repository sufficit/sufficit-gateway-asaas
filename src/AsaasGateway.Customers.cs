using Sufficit.Gateway;
using System.Globalization;
using System.Net;

namespace Sufficit.Gateway.Asaas;

/// <summary>
/// Native Asaas customers API (/v3/customers). GetCustomerAsync is inherited
/// from the invoices partial, which already implements the same contract.
/// </summary>
public sealed partial class AsaasGateway : IAsaasCustomerGateway
{
    public async Task<AsaasCustomer> CreateCustomerAsync(
        AsaasCustomerCreateRequest request,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateContext(context);
        ValidateCustomerName(request.Name);

        using var response = await SendGatewayAsync(
            () => CreateJsonRequest(HttpMethod.Post, BuildUri(context, "customers"), request),
            context,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "customer", cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<AsaasCustomer>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AsaasCustomer> UpdateCustomerAsync(
        string customerId,
        AsaasCustomerUpdateRequest request,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ValidateCustomerId(customerId);
        ArgumentNullException.ThrowIfNull(request);
        ValidateContext(context);
        ValidateCustomerName(request.Name);

        using var response = await SendGatewayAsync(
            () => CreateJsonRequest(
                HttpMethod.Put,
                BuildUri(context, $"customers/{Uri.EscapeDataString(customerId.Trim())}"),
                request),
            context,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "customer", cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<AsaasCustomer>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AsaasCustomerPage> ListCustomersAsync(
        AsaasCustomerSearchParameters parameters,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ValidateContext(context);
        ValidatePageArguments(parameters.Offset, parameters.Limit);

        var query = BuildCustomerQuery(parameters);
        using var response = await SendGatewayAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BuildUri(context, query)),
            context,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "customer", cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<AsaasCustomerPage>(response, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildCustomerQuery(AsaasCustomerSearchParameters parameters)
    {
        var values = new List<KeyValuePair<string, string>>
        {
            new("offset", parameters.Offset.ToString(CultureInfo.InvariantCulture)),
            new("limit", parameters.Limit.ToString(CultureInfo.InvariantCulture))
        };
        AddQueryValue(values, "name", parameters.Name);
        AddQueryValue(values, "cpfCnpj", parameters.CpfCnpj);
        AddQueryValue(values, "email", parameters.Email);
        AddQueryValue(values, "externalReference", parameters.ExternalReference);
        return "customers?" + string.Join(
            "&",
            values.Select(value =>
                $"{Uri.EscapeDataString(value.Key)}={Uri.EscapeDataString(value.Value)}"));
    }

    private static void ValidateCustomerName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A customer name is required.", nameof(name));
        }
    }

    private static void ValidateCustomerId(string customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            throw new ArgumentException("An Asaas customer identifier is required.", nameof(customerId));
        }
    }
}
