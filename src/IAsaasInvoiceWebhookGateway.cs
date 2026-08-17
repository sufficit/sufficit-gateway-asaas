using Sufficit.Gateway;

namespace Sufficit.Gateway.Asaas;

/// <summary>
/// Authenticates and parses invoice webhook events without exposing protected
/// credentials to the API host.
/// </summary>
public interface IAsaasInvoiceWebhookGateway
{
    Task<bool> AuthenticateAsync(
        IReadOnlyDictionary<string, string> requestHeaders,
        GatewayCallContext context,
        CancellationToken cancellationToken);

    AsaasInvoiceWebhookEnvelope Parse(string requestPayload);
}

public sealed class AsaasInvoiceWebhookEnvelope
{
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset EventAtUtc { get; set; }
    public AsaasInvoice Invoice { get; set; } = new();
    public string RawPayload { get; set; } = "{}";
}
