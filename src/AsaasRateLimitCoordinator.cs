using Microsoft.Extensions.Logging;
using Sufficit.Gateway;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace Sufficit.Gateway.Asaas;

internal sealed class AsaasRateLimitCoordinator
{
    private const int ProviderMaximumConcurrentGetRequests = 50;
    private readonly ConcurrentDictionary<RateLimitKey, RateLimitState> _states = new();
    private readonly ILogger<AsaasGateway> _logger;
    private readonly TimeProvider _timeProvider;

    public AsaasRateLimitCoordinator(
        ILogger<AsaasGateway> logger,
        TimeProvider? timeProvider = null)
    {
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IDisposable Admit(
        HttpMethod method,
        GatewayCallContext context,
        AsaasGatewayOptions options)
    {
        var now = _timeProvider.GetUtcNow();
        var state = GetState(context);
        var isGet = method == HttpMethod.Get;
        var maximumConcurrentGets = GetMaximumConcurrentGets(options);
        var localQuotaAllowance = GetLocalQuotaAllowance(options);
        TimeSpan? retryAfter = null;
        string? rejectionCode = null;

        lock (state.SyncRoot)
        {
            RefreshExpiredState(state, options, now);

            if (state.ProviderBlockedUntilUtc > now)
            {
                retryAfter = state.ProviderBlockedUntilUtc - now;
                rejectionCode = "asaas_rate_limit_blocked";
            }
            else if (options.EnforceLocalQuotaLimit
                && state.LocalQuotaUsed >= localQuotaAllowance)
            {
                var resetAt = GetLocalQuotaResetAt(state, options, now);
                retryAfter = resetAt - now;
                rejectionCode = "asaas_local_quota_reserve_reached";
            }
            else if (isGet && state.ConcurrentGetRequests >= maximumConcurrentGets)
            {
                retryAfter = PositiveOrDefault(
                    options.ConcurrentLimitBackoff,
                    TimeSpan.FromSeconds(2));
                rejectionCode = "asaas_local_get_concurrency_reached";
            }

            if (rejectionCode is null)
            {
                state.LocalQuotaWindowStartedAtUtc ??= now;
                state.LocalQuotaUsed++;
                if (isGet)
                    state.ConcurrentGetRequests++;
            }
        }

        if (rejectionCode is not null)
        {
            _logger.LogWarning(
                "Asaas request rejected locally for {Environment}/{CredentialReference}: {Reason}; retry after {RetryAfter}.",
                context.Environment,
                context.CredentialReference,
                rejectionCode,
                retryAfter);
            throw new AsaasGatewayException(
                rejectionCode,
                "The Asaas request was held locally to avoid exceeding provider limits.",
                (int)HttpStatusCode.TooManyRequests,
                retryAfter: retryAfter);
        }

        return new AdmissionLease(() => Release(state, isGet));
    }

    public void Observe(
        HttpRequestMessage request,
        HttpResponseMessage response,
        GatewayCallContext context,
        AsaasGatewayOptions options)
    {
        var now = _timeProvider.GetUtcNow();
        var limit = ReadIntegerHeader(response, "RateLimit-Limit");
        var remaining = ReadIntegerHeader(response, "RateLimit-Remaining");
        var resetSeconds = ReadIntegerHeader(response, "RateLimit-Reset");
        var retryAfter = ReadRetryAfter(response, now);
        var resetDelay = resetSeconds.HasValue
            ? TimeSpan.FromSeconds(Math.Max(0, resetSeconds.Value))
            : retryAfter;
        var resetAt = resetDelay.HasValue
            ? now + LimitBackoff(resetDelay.Value, options)
            : (DateTimeOffset?)null;
        if (resetDelay.HasValue && response.Headers.RetryAfter is null)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(
                LimitBackoff(resetDelay.Value, options));
        }
        var isProviderRejection = response.StatusCode == HttpStatusCode.TooManyRequests
            || (response.StatusCode == HttpStatusCode.Forbidden && resetAt.HasValue)
            || remaining == 0;
        var state = GetState(context);

        lock (state.SyncRoot)
        {
            if (limit.HasValue || remaining.HasValue || resetAt.HasValue)
            {
                state.ProviderLimit = limit ?? state.ProviderLimit;
                state.ProviderRemaining = remaining ?? state.ProviderRemaining;
                state.ProviderResetAtUtc = resetAt ?? state.ProviderResetAtUtc;
                state.ProviderObservedAtUtc = now;
            }

            state.LastRequestPath = request.RequestUri?.PathAndQuery;
            state.LastStatusCode = (int)response.StatusCode;

            if (isProviderRejection)
            {
                var blockedUntil = resetAt
                    ?? now + LimitBackoff(
                        PositiveOrDefault(options.DefaultRateLimitBackoff, TimeSpan.FromMinutes(1)),
                        options);
                if (state.ProviderBlockedUntilUtc is null || blockedUntil > state.ProviderBlockedUntilUtc)
                    state.ProviderBlockedUntilUtc = blockedUntil;
                state.BlockReason = response.StatusCode == HttpStatusCode.TooManyRequests
                    ? "asaas_http_429"
                    : "asaas_provider_remaining_zero";
            }
        }

        if (isProviderRejection)
        {
            _logger.LogWarning(
                "Asaas provider limit reached for {Environment}/{CredentialReference}: status {StatusCode}, limit {Limit}, remaining {Remaining}, reset at {ResetAtUtc}.",
                context.Environment,
                context.CredentialReference,
                (int)response.StatusCode,
                limit,
                remaining,
                resetAt);
        }
        else if (remaining.HasValue && remaining <= Math.Max(0, options.LowRateLimitRemainingThreshold))
        {
            _logger.LogWarning(
                "Asaas provider limit is low for {Environment}/{CredentialReference}: {Remaining} of {Limit} remaining.",
                context.Environment,
                context.CredentialReference,
                remaining,
                limit);
        }
        else if (limit.HasValue || remaining.HasValue || resetAt.HasValue)
        {
            _logger.LogDebug(
                "Asaas limits observed for {Environment}/{CredentialReference}: limit {Limit}, remaining {Remaining}, reset at {ResetAtUtc}.",
                context.Environment,
                context.CredentialReference,
                limit,
                remaining,
                resetAt);
        }
    }

    public AsaasRateLimitSnapshot GetSnapshot(
        GatewayCallContext context,
        AsaasGatewayOptions options)
    {
        var now = _timeProvider.GetUtcNow();
        var state = GetState(context);
        lock (state.SyncRoot)
        {
            RefreshExpiredState(state, options, now);
            var allowance = GetLocalQuotaAllowance(options);
            var localResetAt = state.LocalQuotaWindowStartedAtUtc.HasValue
                ? GetLocalQuotaResetAt(state, options, now)
                : (DateTimeOffset?)null;
            var localBlocked = options.EnforceLocalQuotaLimit
                && state.LocalQuotaUsed >= allowance;
            var providerBlocked = state.ProviderBlockedUntilUtc > now;
            return new AsaasRateLimitSnapshot
            {
                Environment = context.Environment,
                CredentialReference = context.CredentialReference,
                ProviderLimit = state.ProviderLimit,
                ProviderRemaining = state.ProviderRemaining,
                ProviderResetAtUtc = state.ProviderResetAtUtc,
                ProviderObservedAtUtc = state.ProviderObservedAtUtc,
                LastRequestPath = state.LastRequestPath,
                LastStatusCode = state.LastStatusCode,
                LocalQuotaWindowStartedAtUtc = state.LocalQuotaWindowStartedAtUtc,
                LocalQuotaResetAtUtc = localResetAt,
                LocalQuotaUsed = state.LocalQuotaUsed,
                LocalQuotaAllowance = allowance,
                LocalQuotaRemaining = Math.Max(0, allowance - state.LocalQuotaUsed),
                ConcurrentGetRequests = state.ConcurrentGetRequests,
                MaxConcurrentGetRequests = GetMaximumConcurrentGets(options),
                BlockedUntilUtc = providerBlocked
                    ? state.ProviderBlockedUntilUtc
                    : localBlocked
                        ? localResetAt
                        : null,
                BlockReason = providerBlocked
                    ? state.BlockReason
                    : localBlocked
                        ? "asaas_local_quota_reserve_reached"
                        : null,
                IsBlocked = providerBlocked || localBlocked
            };
        }
    }

    private RateLimitState GetState(GatewayCallContext context)
        => _states.GetOrAdd(
            new RateLimitKey(context.Environment, context.CredentialReference),
            static _ => new RateLimitState());

    private static void Release(RateLimitState state, bool isGet)
    {
        if (!isGet)
            return;

        lock (state.SyncRoot)
        {
            if (state.ConcurrentGetRequests > 0)
                state.ConcurrentGetRequests--;
        }
    }

    private static void RefreshExpiredState(
        RateLimitState state,
        AsaasGatewayOptions options,
        DateTimeOffset now)
    {
        if (state.LocalQuotaWindowStartedAtUtc.HasValue
            && GetLocalQuotaResetAt(state, options, now) <= now)
        {
            state.LocalQuotaWindowStartedAtUtc = null;
            state.LocalQuotaUsed = 0;
        }

        if (state.ProviderBlockedUntilUtc <= now)
        {
            state.ProviderBlockedUntilUtc = null;
            state.BlockReason = null;
        }
    }

    private static DateTimeOffset GetLocalQuotaResetAt(
        RateLimitState state,
        AsaasGatewayOptions options,
        DateTimeOffset now)
        => (state.LocalQuotaWindowStartedAtUtc ?? now)
            + PositiveOrDefault(options.QuotaWindow, TimeSpan.FromHours(12));

    private static int GetMaximumConcurrentGets(AsaasGatewayOptions options)
        => Math.Clamp(options.MaxConcurrentGetRequests, 1, ProviderMaximumConcurrentGetRequests);

    private static int GetLocalQuotaAllowance(AsaasGatewayOptions options)
    {
        var limit = Math.Max(1, options.QuotaLimit);
        var reserve = Math.Clamp(options.QuotaReserve, 0, limit - 1);
        return limit - reserve;
    }

    private static TimeSpan LimitBackoff(TimeSpan value, AsaasGatewayOptions options)
    {
        var maximum = PositiveOrDefault(options.MaximumProviderBackoff, TimeSpan.FromHours(12));
        return value > maximum ? maximum : PositiveOrDefault(value, TimeSpan.FromSeconds(1));
    }

    private static TimeSpan PositiveOrDefault(TimeSpan value, TimeSpan fallback)
        => value > TimeSpan.Zero ? value : fallback;

    private static int? ReadIntegerHeader(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values))
            return null;

        var value = values.FirstOrDefault();
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return delta;
        if (retryAfter?.Date is { } date && date > now)
            return date - now;
        return null;
    }

    private readonly record struct RateLimitKey(
        GatewayEnvironment Environment,
        string CredentialReference);

    private sealed class RateLimitState
    {
        public object SyncRoot { get; } = new();
        public int? ProviderLimit { get; set; }
        public int? ProviderRemaining { get; set; }
        public DateTimeOffset? ProviderResetAtUtc { get; set; }
        public DateTimeOffset? ProviderObservedAtUtc { get; set; }
        public string? LastRequestPath { get; set; }
        public int? LastStatusCode { get; set; }
        public DateTimeOffset? LocalQuotaWindowStartedAtUtc { get; set; }
        public int LocalQuotaUsed { get; set; }
        public int ConcurrentGetRequests { get; set; }
        public DateTimeOffset? ProviderBlockedUntilUtc { get; set; }
        public string? BlockReason { get; set; }
    }

    private sealed class AdmissionLease : IDisposable
    {
        private Action? _release;

        public AdmissionLease(Action release)
        {
            _release = release;
        }

        public void Dispose()
            => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
