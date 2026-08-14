using Sufficit.Gateway;

namespace Sufficit.Gateway.Asaas;

/// <summary>
/// Read-only operational view of Asaas limits for one credential reference.
/// Provider values come from response headers; local quota values only include
/// requests dispatched by the current process.
/// </summary>
public sealed class AsaasRateLimitSnapshot
{
    public GatewayEnvironment Environment { get; init; }
    public string CredentialReference { get; init; } = string.Empty;
    public int? ProviderLimit { get; init; }
    public int? ProviderRemaining { get; init; }
    public DateTimeOffset? ProviderResetAtUtc { get; init; }
    public DateTimeOffset? ProviderObservedAtUtc { get; init; }
    public string? LastRequestPath { get; init; }
    public int? LastStatusCode { get; init; }
    public DateTimeOffset? LocalQuotaWindowStartedAtUtc { get; init; }
    public DateTimeOffset? LocalQuotaResetAtUtc { get; init; }
    public int LocalQuotaUsed { get; init; }
    public int LocalQuotaAllowance { get; init; }
    public int LocalQuotaRemaining { get; init; }
    public int ConcurrentGetRequests { get; init; }
    public int MaxConcurrentGetRequests { get; init; }
    public DateTimeOffset? BlockedUntilUtc { get; init; }
    public string? BlockReason { get; init; }
    public bool IsBlocked { get; init; }
}

/// <summary>
/// Exposes rate-limit state without allowing callers to mutate admission.
/// </summary>
public interface IAsaasRateLimitMonitor
{
    AsaasRateLimitSnapshot GetRateLimitSnapshot(GatewayCallContext context);
}

public sealed partial class AsaasGateway : IAsaasRateLimitMonitor
{
    public AsaasRateLimitSnapshot GetRateLimitSnapshot(GatewayCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.CredentialReference))
            throw new ArgumentException("Credential reference is required.", nameof(context));

        return _rateLimits.GetSnapshot(context, _options.CurrentValue);
    }
}
