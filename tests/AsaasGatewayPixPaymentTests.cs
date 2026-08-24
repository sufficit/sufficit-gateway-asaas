using Sufficit.Finance;
using Sufficit.Gateway;
using System.Text.Json;
using Xunit;

namespace Sufficit.Gateway.Asaas.Tests;

public sealed class AsaasGatewayPixPaymentTests
{
    private const string TinyPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9WlK8akAAAAASUVORK5CYII=";

    [Fact]
    public async Task CreateUsesMappedCustomerAndReturnsInlinePixPresentation()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson("""{"object":"list","hasMore":false,"data":[]}""");
        handler.EnqueueJson(
            """{"object":"payment","id":"pay_pix","status":"PENDING","externalReference":"checkout:8c732677a5ea4f33a8e13dfcdb538411"}""");
        handler.EnqueueJson(
            $$"""{"encodedImage":"{{TinyPng}}","payload":"00020126580014BR.GOV.BCB.PIX0136test-pix-payload-12345678901234567890","expirationDate":"2026-08-25 15:00:00"}""");
        IPixPaymentGateway gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.CreateAsync(
            CreateRequest(new PixPaymentPayer { ProviderCustomerId = "cus_test" }),
            CreateContext(),
            CancellationToken.None);

        Assert.Equal("pay_pix", result.ChargeId);
        Assert.Equal(PixPaymentStatus.AwaitingPayment, result.Status);
        Assert.StartsWith("000201", result.CopyAndPaste);
        Assert.StartsWith("data:image/png;base64,", result.QrCodeImageDataUri);
        Assert.NotNull(result.ExpiresAt);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.Equal("https://api-sandbox.asaas.com/v3/payments", handler.Requests[1].Uri.AbsoluteUri);

        using var payload = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.Equal("cus_test", payload.RootElement.GetProperty("customer").GetString());
        Assert.Equal("PIX", payload.RootElement.GetProperty("billingType").GetString());
        Assert.Equal(159.90m, payload.RootElement.GetProperty("value").GetDecimal());
        Assert.Equal(
            "checkout:8c732677a5ea4f33a8e13dfcdb538411",
            payload.RootElement.GetProperty("externalReference").GetString());
    }

    [Fact]
    public async Task CreateResolvesCustomerByProtectedPayerSnapshot()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson("""{"object":"list","hasMore":false,"data":[]}""");
        handler.EnqueueJson("""{"object":"list","hasMore":false,"data":[]}""");
        handler.EnqueueJson("""{"object":"customer","id":"cus_created"}""");
        handler.EnqueueJson(
            """{"object":"payment","id":"pay_created","status":"PENDING","externalReference":"checkout:8c732677a5ea4f33a8e13dfcdb538411"}""");
        handler.EnqueueJson(
            $$"""{"encodedImage":"{{TinyPng}}","payload":"00020126580014BR.GOV.BCB.PIX0136test-pix-payload-12345678901234567890"}""");
        IPixPaymentGateway gateway = GatewayTestFactory.CreateAsaas(handler);

        await gateway.CreateAsync(
            CreateRequest(new PixPaymentPayer
            {
                Name = "Cliente Teste",
                Document = "529.982.247-25",
                Email = "cliente@example.test"
            }),
            CreateContext(),
            CancellationToken.None);

        Assert.Equal(5, handler.Requests.Count);
        Assert.StartsWith(
            "https://api-sandbox.asaas.com/v3/customers?cpfCnpj=52998224725",
            handler.Requests[1].Uri.AbsoluteUri);
        Assert.Equal(HttpMethod.Post, handler.Requests[2].Method);
        Assert.Contains("\"notificationDisabled\":true", handler.Requests[2].Body);
        Assert.DoesNotContain("529.982", handler.Requests[2].Body);
    }

    [Fact]
    public async Task CreateReusesExistingChargeByExternalReference()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson(
            """{"object":"list","data":[{"id":"pay_existing","status":"PENDING","externalReference":"checkout:8c732677a5ea4f33a8e13dfcdb538411"}]}""");
        handler.EnqueueJson(
            $$"""{"encodedImage":"{{TinyPng}}","payload":"00020126580014BR.GOV.BCB.PIX0136test-pix-payload-12345678901234567890"}""");
        IPixPaymentGateway gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.CreateAsync(
            CreateRequest(new PixPaymentPayer { ProviderCustomerId = "cus_test" }),
            CreateContext(),
            CancellationToken.None);

        Assert.Equal("pay_existing", result.ChargeId);
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public void ParseWebhookMapsPaymentWithoutExposingProviderToConsumer()
    {
        IPixPaymentGateway gateway = GatewayTestFactory.CreateAsaas(new RecordingHttpMessageHandler());

        var notification = gateway.ParseWebhook(
            """
            {
              "id":"evt_payment_received",
              "event":"PAYMENT_RECEIVED",
              "dateCreated":"2026-08-24 15:30:00",
              "payment":{
                "id":"pay_pix",
                "billingType":"PIX",
                "status":"RECEIVED",
                "externalReference":"checkout:8c732677a5ea4f33a8e13dfcdb538411",
                "value":159.90,
                "paymentDate":"2026-08-24"
              }
            }
            """);

        Assert.Equal("evt_payment_received", notification.EventId);
        Assert.Equal("pay_pix", notification.ChargeId);
        Assert.Equal(PixPaymentStatus.Paid, notification.Status);
        Assert.Equal(159.90m, notification.Value);
        Assert.NotNull(notification.PaidAt);
    }

    private static PixPaymentRequest CreateRequest(PixPaymentPayer payer) => new()
    {
        PaymentId = Guid.Parse("8c732677-a5ea-4f33-a8e1-3dfcdb538411"),
        ContextId = Guid.Parse("d9f76c63-e026-489b-9fd2-e3f5210dd8ac"),
        Value = 159.90m,
        DueDate = new DateTime(2026, 8, 31),
        Description = "Sufficit Cloud Mobile Small",
        Payer = payer
    };

    private static GatewayCallContext CreateContext() => new()
    {
        TenantId = OSInformation.SufficitId,
        Environment = GatewayEnvironment.Sandbox,
        CredentialReference = "tests/asaas"
    };
}
