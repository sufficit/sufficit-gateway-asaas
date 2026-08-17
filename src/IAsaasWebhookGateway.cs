using Sufficit.Gateway;

namespace Sufficit.Gateway.Asaas;

/// <summary>
/// Manages provider-wide Asaas webhook subscriptions. This capability is kept
/// separate from bank-slip issuance because the same Asaas account can publish
/// events for payments, invoices and future gateway modules.
/// </summary>
public interface IAsaasWebhookGateway
{
    Task<IReadOnlyList<AsaasWebhookSubscription>> ListAsync(
        GatewayCallContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates the named subscription when absent and updates it when its
    /// observable configuration differs. The authentication token is resolved
    /// from the protected credential reference and never crosses this boundary.
    /// </summary>
    Task<AsaasWebhookProvisioningResult> EnsureAsync(
        AsaasWebhookProvisioningRequest request,
        GatewayCallContext context,
        CancellationToken cancellationToken);
}

public sealed class AsaasWebhookProvisioningRequest
{
    public string Name { get; set; } = string.Empty;
    public Uri Url { get; set; } = null!;
    public string NotificationEmail { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool ForceUpdate { get; set; }
    public IReadOnlyCollection<string> Events { get; set; } = Array.Empty<string>();
}

public sealed class AsaasWebhookProvisioningResult
{
    public AsaasWebhookProvisioningOutcome Outcome { get; set; }
    public AsaasWebhookSubscription Subscription { get; set; } = new();
}

public enum AsaasWebhookProvisioningOutcome : byte
{
    Unchanged = 0,
    Created = 1,
    Updated = 2
}

public sealed class AsaasWebhookSubscription
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Uri? Url { get; set; }
    public string? NotificationEmail { get; set; }
    public bool Enabled { get; set; }
    public bool Interrupted { get; set; }
    public string SendType { get; set; } = string.Empty;
    public IReadOnlyCollection<string> Events { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Minimal payment-event set required by the bank-slip lifecycle. Delivery is
/// sequential so provider order is preserved; consumers remain idempotent.
/// </summary>
public static class AsaasWebhookEventSets
{
    public static IReadOnlyCollection<string> InvoiceLifecycle { get; } = new[]
    {
        "INVOICE_AUTHORIZED",
        "INVOICE_CANCELED"
    };

    public static IReadOnlyCollection<string> BankSlipLifecycle { get; } = new[]
    {
        "PAYMENT_CREATED",
        "PAYMENT_UPDATED",
        "PAYMENT_CONFIRMED",
        "PAYMENT_RECEIVED",
        "PAYMENT_OVERDUE",
        "PAYMENT_DELETED",
        "PAYMENT_RESTORED",
        "PAYMENT_REFUNDED",
        "PAYMENT_PARTIALLY_REFUNDED",
        "PAYMENT_REFUND_IN_PROGRESS",
        "PAYMENT_REFUND_DENIED",
        "PAYMENT_RECEIVED_IN_CASH_UNDONE",
        "PAYMENT_BANK_SLIP_CANCELLED"
    };
}
