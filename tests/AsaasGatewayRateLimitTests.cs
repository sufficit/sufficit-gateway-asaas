using Sufficit.Gateway;
using System.Net;
using System.Text;
using Xunit;

namespace Sufficit.Gateway.Asaas.Tests;

public sealed class AsaasGatewayRateLimitTests
{
    private const string EmptyInvoicePage =
        """{"object":"list","data":[],"hasMore":false,"totalCount":0,"limit":10,"offset":0}""";

    [Fact]
    public async Task ProviderHeadersOpenLocalCircuitAndExposeAuthoritativeSnapshot()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson(
            """{"errors":[{"code":"rate_limit","description":"Too many requests"}]}""",
            HttpStatusCode.TooManyRequests,
            new Dictionary<string, string>
            {
                ["RateLimit-Limit"] = "100",
                ["RateLimit-Remaining"] = "0",
                ["RateLimit-Reset"] = "30"
            });
        var gateway = GatewayTestFactory.CreateAsaas(handler);
        var context = CreateContext();

        var providerException = await Assert.ThrowsAsync<AsaasGatewayException>(
            () => ListAsync(gateway, context));
        var localException = await Assert.ThrowsAsync<AsaasGatewayException>(
            () => ListAsync(gateway, context));
        var snapshot = gateway.GetRateLimitSnapshot(context);

        Assert.Equal("rate_limit", providerException.ErrorCode);
        Assert.True(providerException.RetryAfter > TimeSpan.Zero);
        Assert.Equal("asaas_rate_limit_blocked", localException.ErrorCode);
        Assert.Equal(429, localException.HttpStatusCode);
        Assert.True(localException.RetryAfter > TimeSpan.Zero);
        Assert.Single(handler.Requests);
        Assert.Equal(100, snapshot.ProviderLimit);
        Assert.Equal(0, snapshot.ProviderRemaining);
        Assert.Equal(1, snapshot.LocalQuotaUsed);
        Assert.True(snapshot.IsBlocked);
        Assert.Equal("asaas_http_429", snapshot.BlockReason);
        Assert.True(snapshot.ProviderResetAtUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task LocalQuotaCountsDispatchedCallsAndKeepsConfiguredReserve()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson(EmptyInvoicePage);
        handler.EnqueueJson(EmptyInvoicePage);
        var gateway = GatewayTestFactory.CreateAsaas(handler, options =>
        {
            options.QuotaLimit = 3;
            options.QuotaReserve = 1;
        });
        var context = CreateContext();

        await ListAsync(gateway, context);
        await ListAsync(gateway, context);
        var exception = await Assert.ThrowsAsync<AsaasGatewayException>(
            () => ListAsync(gateway, context));
        var snapshot = gateway.GetRateLimitSnapshot(context);

        Assert.Equal("asaas_local_quota_reserve_reached", exception.ErrorCode);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(2, snapshot.LocalQuotaAllowance);
        Assert.Equal(2, snapshot.LocalQuotaUsed);
        Assert.Equal(0, snapshot.LocalQuotaRemaining);
        Assert.True(snapshot.IsBlocked);
    }

    [Fact]
    public async Task ConcurrentGetGuardFailsFastBeforeDispatchingAnotherRequest()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueResponse(async (_, cancellationToken) =>
        {
            entered.SetResult(true);
            await release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(EmptyInvoicePage, Encoding.UTF8, "application/json")
            };
        });
        var gateway = GatewayTestFactory.CreateAsaas(
            handler,
            options => options.MaxConcurrentGetRequests = 1);
        var context = CreateContext();

        var firstRequest = ListAsync(gateway, context);
        await entered.Task;

        var exception = await Assert.ThrowsAsync<AsaasGatewayException>(
            () => ListAsync(gateway, context));
        var duringRequest = gateway.GetRateLimitSnapshot(context);
        release.SetResult(true);
        await firstRequest;
        var afterRequest = gateway.GetRateLimitSnapshot(context);

        Assert.Equal("asaas_local_get_concurrency_reached", exception.ErrorCode);
        Assert.Single(handler.Requests);
        Assert.Equal(1, duringRequest.ConcurrentGetRequests);
        Assert.Equal(0, afterRequest.ConcurrentGetRequests);
        Assert.Equal(1, afterRequest.LocalQuotaUsed);
    }

    private static Task<AsaasInvoicePage> ListAsync(
        AsaasGateway gateway,
        GatewayCallContext context)
        => gateway.ListInvoicesAsync(
            new AsaasInvoiceSearchParameters { Limit = 10 },
            context,
            CancellationToken.None);

    private static GatewayCallContext CreateContext()
        => new()
        {
            TenantId = OSInformation.SufficitId,
            Environment = GatewayEnvironment.Sandbox,
            CredentialReference = "tests/asaas"
        };
}
