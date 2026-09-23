using Sufficit.Gateway;

namespace Sufficit.Gateway.Asaas;

/// <summary>
/// Typed access to the native Asaas customers API (/v3/customers). Speaks
/// only Asaas vocabulary; customer-to-domain mapping belongs to consumers.
/// </summary>
public interface IAsaasCustomerGateway
{
    Task<AsaasCustomer> CreateCustomerAsync(
        AsaasCustomerCreateRequest request,
        GatewayCallContext context,
        CancellationToken cancellationToken);

    Task<AsaasCustomer> UpdateCustomerAsync(
        string customerId,
        AsaasCustomerUpdateRequest request,
        GatewayCallContext context,
        CancellationToken cancellationToken);

    Task<AsaasCustomer?> GetCustomerAsync(
        string customerId,
        GatewayCallContext context,
        CancellationToken cancellationToken);

    Task<AsaasCustomerPage> ListCustomersAsync(
        AsaasCustomerSearchParameters parameters,
        GatewayCallContext context,
        CancellationToken cancellationToken);
}
