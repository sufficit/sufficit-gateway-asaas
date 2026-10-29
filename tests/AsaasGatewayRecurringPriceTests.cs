using Sufficit.Gateway;
using Xunit;
using System.Net;
using System.Text.Json;

namespace Sufficit.Gateway.Asaas.Tests;

public sealed class AsaasGatewayRecurringPriceTests
{
    private const string Checkout = "5dd6d187-8db2-446e-8912-c5f298860241";
    private static GatewayCallContext Context() => new() { TenantId = OSInformation.SufficitId,
        Environment = GatewayEnvironment.Sandbox, CredentialReference = "tests/asaas" };
    private static void Snapshot(RecordingHttpMessageHandler h, decimal value, string firstStatus, decimal futureValue = 397.60m)
    {
        h.EnqueueJson(JsonSerializer.Serialize(new { data = new[] {
            new { id="pay_first", customer="cus_test", billingType="CREDIT_CARD", subscription="sub_test", checkoutSession=Checkout, value=397.60m, status=firstStatus, dueDate="2026-10-22" },
            new { id="pay_next", customer="cus_test", billingType="CREDIT_CARD", subscription="sub_test", checkoutSession=Checkout, value=futureValue, status="PENDING", dueDate="2026-11-22" }
        }, hasMore=false }));
        h.EnqueueJson(JsonSerializer.Serialize(new { customer="cus_test", billingType="CREDIT_CARD", cycle="MONTHLY", status="ACTIVE", value, nextDueDate="2026-11-22" }));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HostedPayerRequiresExactCheckoutBinding(bool matches)
    {
        var h = new RecordingHttpMessageHandler();
        h.EnqueueJson(JsonSerializer.Serialize(new { data = new[] { new { customer = "cus_edited", checkoutSession = matches ? Checkout : Guid.NewGuid().ToString(),
            billingType = "CREDIT_CARD", subscription = "sub_hosted" } }, hasMore = false }));
        var task = GatewayTestFactory.CreateAsaas(h).GetRecurringCustomerAsync(Checkout, Context(), default);
        if (matches) Assert.Equal("cus_edited", await task);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => task);
    }

    [Fact]
    public async Task EmptyCheckoutFilterRecoversExactCheckoutFromPaginatedCustomerPayments()
    {
        var h = new RecordingHttpMessageHandler();
        h.EnqueueJson("""{"data":[],"hasMore":false}""");
        h.EnqueueJson("""{"data":[{"customer":"cus_test","billingType":"PIX","checkoutSession":"another-checkout"}],"hasMore":true}""");
        h.EnqueueJson(JsonSerializer.Serialize(new { data = new[] {
            new { id="pay_first", customer="cus_test", billingType="CREDIT_CARD", subscription="sub_test", checkoutSession=Checkout, value=12.34m, status="CONFIRMED", dueDate="2026-10-19" }
        }, hasMore=false }));
        h.EnqueueJson("""{"customer":"cus_test","billingType":"CREDIT_CARD","cycle":"MONTHLY","status":"ACTIVE","value":12.34,"nextDueDate":"2026-11-19"}""");
        var result = await GatewayTestFactory.CreateAsaas(h).GetRecurringSnapshotAsync(Checkout,"cus_test",null,Context(),default);
        Assert.Equal("sub_test", result.SubscriptionId);
        Assert.Equal("pay_first", Assert.Single(result.Payments).Id);
        Assert.Equal(4, h.Requests.Count);
        Assert.DoesNotContain("checkoutSession=", h.Requests[1].Uri.Query);
        Assert.Contains("customer=cus_test", h.Requests[1].Uri.Query);
        Assert.Contains("offset=100", h.Requests[2].Uri.Query);
        Assert.All(h.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
    }

    [Fact]
    public async Task CustomerRecoveryNeverAdoptsAnotherCheckoutSubscription()
    {
        var h = new RecordingHttpMessageHandler();
        h.EnqueueJson("""{"data":[],"hasMore":false}""");
        h.EnqueueJson("""{"data":[{"customer":"cus_test","billingType":"CREDIT_CARD","checkoutSession":"another-checkout","subscription":"sub_other"}],"hasMore":false}""");
        var result = await GatewayTestFactory.CreateAsaas(h).GetRecurringSnapshotAsync(Checkout,"cus_test",null,Context(),default);
        Assert.Null(result.SubscriptionId);
        Assert.Empty(result.Payments);
        Assert.Equal(2, h.Requests.Count);
    }

    [Fact]
    public async Task ConfirmedInitialPaymentIsPreservedWhilePendingFutureChargesAdoptMonthlyPrice()
    {
        var h=new RecordingHttpMessageHandler();
        Snapshot(h,397.60m,"CONFIRMED"); Snapshot(h,397.60m,"CONFIRMED");
        h.EnqueueJson("{}"); h.EnqueueJson("{}"); Snapshot(h,389.60m,"CONFIRMED",389.60m);
        var result=await GatewayTestFactory.CreateAsaas(h).ReconcileRecurringAmountAsync(Checkout,"cus_test",null,
            397.60m,new(2026,10,22),389.60m,new(2026,11,22),Context(),default);
        Assert.Equal(389.60m,result.Value);
        Assert.Equal(397.60m,result.Payments.Single(x=>x.Id=="pay_first").Value);
        var write=Assert.Single(h.Requests,x=>x.Method==HttpMethod.Put && x.Uri.AbsolutePath.Contains("subscriptions/"));
        using var json=JsonDocument.Parse(write.Body!);
        Assert.Equal(389.60m,json.RootElement.GetProperty("value").GetDecimal());
        Assert.False(json.RootElement.GetProperty("updatePendingPayments").GetBoolean());
        Assert.DoesNotContain(h.Requests,x=>x.Method==HttpMethod.Put && x.Uri.AbsolutePath.EndsWith("pay_first"));
        Assert.False(json.RootElement.TryGetProperty("endDate",out _));
    }
    [Fact]
    public async Task PendingFirstChargeIsPreservedAndOnlyFutureChargesAreRepriced()
    {
        var h=new RecordingHttpMessageHandler();Snapshot(h,397.60m,"PENDING");Snapshot(h,397.60m,"PENDING");
        h.EnqueueJson("{}");h.EnqueueJson("{}");Snapshot(h,389.60m,"PENDING",389.60m);
        var result=await GatewayTestFactory.CreateAsaas(h).ReconcileRecurringAmountAsync(Checkout,"cus_test",null,
            397.60m,new(2026,10,22),389.60m,new(2026,11,22),Context(),default);
        Assert.Equal(389.60m,result.Value);Assert.Equal(397.60m,result.Payments.Single(x=>x.Id=="pay_first").Value);
        Assert.DoesNotContain(h.Requests,x=>x.Method==HttpMethod.Put && x.Uri.AbsolutePath.EndsWith("pay_first"));
        Assert.Contains(h.Requests,x=>x.Method==HttpMethod.Put && x.Uri.AbsolutePath.EndsWith("pay_next"));
    }
    [Fact]
    public async Task AlreadyNormalizedScheduleDoesNotWriteAgain()
    {
        var h=new RecordingHttpMessageHandler();Snapshot(h,389.60m,"CONFIRMED",389.60m);Snapshot(h,389.60m,"CONFIRMED",389.60m);
        await GatewayTestFactory.CreateAsaas(h).ReconcileRecurringAmountAsync(Checkout,"cus_test","sub_test",
            397.60m,new(2026,10,22),389.60m,new(2026,11,22),Context(),default);
        Assert.All(h.Requests,x=>Assert.Equal(HttpMethod.Get,x.Method));
    }
    [Fact]
    public async Task RejectedUpdateCanBeRetriedWithoutChangingTheInitialReceipt()
    {
        var h=new RecordingHttpMessageHandler();Snapshot(h,397.60m,"CONFIRMED");Snapshot(h,397.60m,"CONFIRMED");
        h.EnqueueJson("{}",HttpStatusCode.BadRequest);
        var gateway=GatewayTestFactory.CreateAsaas(h);
        await Assert.ThrowsAsync<HttpRequestException>(()=>gateway.ReconcileRecurringAmountAsync(Checkout,"cus_test",null,
            397.60m,new(2026,10,22),389.60m,new(2026,11,22),Context(),default));
        Snapshot(h,397.60m,"CONFIRMED");Snapshot(h,397.60m,"CONFIRMED");h.EnqueueJson("{}");h.EnqueueJson("{}");Snapshot(h,389.60m,"CONFIRMED",389.60m);
        var retried=await gateway.ReconcileRecurringAmountAsync(Checkout,"cus_test",null,
            397.60m,new(2026,10,22),389.60m,new(2026,11,22),Context(),default);
        Assert.Equal(389.60m,retried.Value);Assert.Equal(397.60m,retried.Payments.Single(x=>x.Id=="pay_first").Value);
    }

    [Fact]
    public async Task DifferentSubscriptionIsRejectedBeforeAnyWrite()
    {
        var h=new RecordingHttpMessageHandler();Snapshot(h,397.60m,"CONFIRMED");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>GatewayTestFactory.CreateAsaas(h).ReconcileRecurringAmountAsync(Checkout,"cus_test","sub_other",
            397.60m,new(2026,10,22),389.60m,new(2026,11,22),Context(),default));
        Assert.All(h.Requests,x=>Assert.Equal(HttpMethod.Get,x.Method));
    }
}
