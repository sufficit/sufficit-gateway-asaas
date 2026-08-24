using Sufficit.Gateway;

namespace Sufficit.Gateway.Asaas;

/// <summary>
/// Creates hosted Asaas Checkout sessions and validates their lifecycle webhooks.
/// Cardholder and payer data remain inside the provider-hosted experience.
/// </summary>
public interface IAsaasCheckoutGateway
{
    Task<AsaasCheckoutAccount> GetAccountAsync(
        GatewayCallContext context,
        CancellationToken cancellationToken);

    Task<AsaasCheckoutResult> CreateAsync(
        AsaasCheckoutRequest request,
        GatewayCallContext context,
        CancellationToken cancellationToken);

    Task<bool> AuthenticateWebhookAsync(
        IReadOnlyDictionary<string, string> requestHeaders,
        GatewayCallContext context,
        CancellationToken cancellationToken);

    AsaasCheckoutWebhookEvent ParseCheckoutWebhook(string requestPayload);
}

public sealed class AsaasCheckoutAccount
{
    public string Document { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string? PersonType { get; set; }
    public string? Status { get; set; }
}

public enum AsaasCheckoutBillingType : byte
{
    Pix = 0,
    CreditCard = 1
}

public sealed class AsaasCheckoutRequest
{
    public IReadOnlyCollection<AsaasCheckoutBillingType> BillingTypes { get; set; } = [];
    public int MinutesToExpire { get; set; } = 60;
    public string ExternalReference { get; set; } = string.Empty;
    public Uri SuccessUrl { get; set; } = null!;
    public Uri CancelUrl { get; set; } = null!;
    public Uri ExpiredUrl { get; set; } = null!;
    public IReadOnlyCollection<AsaasCheckoutItem> Items { get; set; } = [];
}

public sealed class AsaasCheckoutItem
{
    public string? ExternalReference { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal Value { get; set; }
}

public sealed class AsaasCheckoutResult
{
    public string Id { get; set; } = string.Empty;
    public Uri Link { get; set; } = null!;
    public string Status { get; set; } = string.Empty;
    public string? ExternalReference { get; set; }
}

public sealed class AsaasCheckoutWebhookEvent
{
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset? CreatedAt { get; set; }
    public string CheckoutId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ExternalReference { get; set; }
}
