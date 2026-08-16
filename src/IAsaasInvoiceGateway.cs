using Sufficit.Gateway;

namespace Sufficit.Gateway.Asaas;

/// <summary>
/// Typed access to the Asaas NFS-e lifecycle.
/// </summary>
public interface IAsaasInvoiceGateway
{
    Task<AsaasInvoicePage> ListInvoicesAsync(
        AsaasInvoiceSearchParameters parameters,
        GatewayCallContext context,
        CancellationToken cancellationToken);

    Task<AsaasInvoice?> GetInvoiceAsync(
        string invoiceId,
        GatewayCallContext context,
        CancellationToken cancellationToken);

    Task<AsaasInvoice> ScheduleInvoiceAsync(
        AsaasInvoiceScheduleRequest request,
        GatewayCallContext context,
        CancellationToken cancellationToken);

    Task<AsaasInvoice> UpdateInvoiceAsync(
        string invoiceId,
        AsaasInvoiceUpdateRequest request,
        GatewayCallContext context,
        CancellationToken cancellationToken);

    Task<AsaasInvoice> AuthorizeInvoiceAsync(
        string invoiceId,
        GatewayCallContext context,
        CancellationToken cancellationToken);

    Task<AsaasInvoice> CancelInvoiceAsync(
        string invoiceId,
        AsaasInvoiceCancelRequest request,
        GatewayCallContext context,
        CancellationToken cancellationToken);
}
