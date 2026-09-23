using Sufficit.Gateway;

namespace Sufficit.Gateway.Asaas;

/// <summary>
/// Typed access to the native Asaas payments API (/v3/payments), including
/// boleto and Pix presentation endpoints. Speaks only Asaas vocabulary.
/// </summary>
public interface IAsaasPaymentGateway
{
    Task<AsaasPayment> CreatePaymentAsync(
        AsaasPaymentCreateRequest request,
        GatewayCallContext context,
        CancellationToken cancellationToken);

    Task<AsaasPayment?> GetPaymentAsync(
        string paymentId,
        GatewayCallContext context,
        CancellationToken cancellationToken);

    Task<AsaasPaymentPage> ListPaymentsAsync(
        AsaasPaymentSearchParameters parameters,
        GatewayCallContext context,
        CancellationToken cancellationToken);

    Task<AsaasPaymentStatus?> GetPaymentStatusAsync(
        string paymentId,
        GatewayCallContext context,
        CancellationToken cancellationToken);

    Task<AsaasIdentificationField?> GetIdentificationFieldAsync(
        string paymentId,
        GatewayCallContext context,
        CancellationToken cancellationToken);

    Task<AsaasPixQrCode?> GetPixQrCodeAsync(
        string paymentId,
        GatewayCallContext context,
        CancellationToken cancellationToken);

    Task<AsaasDeletionResult> DeletePaymentAsync(
        string paymentId,
        GatewayCallContext context,
        CancellationToken cancellationToken);
}
