using Sufficit.Finance;
using Xunit;

namespace Sufficit.Gateway.Asaas.Tests;

public sealed class AsaasGatewayWebhookTests
{
    private static readonly Guid BankSlipId = Guid.Parse("8c732677-a5ea-4f33-a8e1-3dfcdb538411");

    [Fact]
    public async Task AuthenticateWebhookAcceptsOnlyConfiguredSecret()
    {
        var gateway = GatewayTestFactory.CreateAsaas(new RecordingHttpMessageHandler());
        var context = CreateContext();

        var accepted = await gateway.AuthenticateWebhookAsync(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ASAAS-ACCESS-TOKEN"] = "0123456789abcdef0123456789abcdef"
            },
            context,
            CancellationToken.None);
        var rejected = await gateway.AuthenticateWebhookAsync(
            new Dictionary<string, string>
            {
                [AsaasGateway.WebhookAuthenticationHeader] = "wrong-secret"
            },
            context,
            CancellationToken.None);

        Assert.True(accepted);
        Assert.False(rejected);
    }

    [Fact]
    public void ParseWebhookMapsPaidBoletoToProviderNeutralEvent()
    {
        var gateway = GatewayTestFactory.CreateAsaas(new RecordingHttpMessageHandler());
        var payload = $$"""
            {
              "id": "evt_payment_received_1",
              "event": "PAYMENT_RECEIVED",
              "dateCreated": "2026-08-14T10:30:00Z",
              "payment": {
                "id": "pay_test",
                "billingType": "BOLETO",
                "status": "RECEIVED",
                "externalReference": "{{BankSlipId:N}}",
                "value": 125.50,
                "clientPaymentDate": "2026-08-14"
              }
            }
            """;

        var envelope = gateway.ParseWebhook(payload);

        Assert.Equal("evt_payment_received_1", envelope.NotificationId);
        Assert.Equal(BankSlipId, envelope.BankSlipId);
        var providerEvent = Assert.Single(envelope.Batch.Events);
        Assert.Equal("pay_test", providerEvent.ChargeId);
        Assert.Equal("charge", providerEvent.EventType);
        Assert.Equal(BankSlipStatus.Paid, providerEvent.Status);
        Assert.Equal(125.50m, providerEvent.Value);
        Assert.NotNull(providerEvent.PaidAtUtc);
    }

    [Fact]
    public void ParseWebhookCarriesOriginOfPaymentWithoutReference()
    {
        var gateway = GatewayTestFactory.CreateAsaas(new RecordingHttpMessageHandler());
        const string payload = """
            {
              "id": "evt_ted_received",
              "event": "PAYMENT_RECEIVED",
              "dateCreated": "2026-10-01T12:12:37Z",
              "payment": {
                "id": "pay_uug5oddu2etclvpk",
                "customer": "cus_000041848192",
                "billingType": "TRANSFER",
                "status": "RECEIVED",
                "value": 500.0,
                "description": "Cobrança gerada automaticamente a partir de TED recebido.",
                "externalReference": null,
                "invoiceUrl": "https://www.asaas.com/i/uug5oddu2etclvpk",
                "transactionReceiptUrl": "https://www.asaas.com/comprovantes/abc",
                "paymentDate": "2026-10-01"
              }
            }
            """;

        var providerEvent = Assert.Single(gateway.ParseWebhook(payload).Batch.Events);

        Assert.Equal(Guid.Empty, gateway.ParseWebhook(payload).BankSlipId);
        Assert.Equal("payment", providerEvent.EventType);
        Assert.Equal(BankSlipStatus.Paid, providerEvent.Status);
        Assert.Equal("TRANSFER", providerEvent.PaymentMethod);
        Assert.Equal("cus_000041848192", providerEvent.PayerReference);
        Assert.Equal("Cobrança gerada automaticamente a partir de TED recebido.", providerEvent.Description);
        Assert.Null(providerEvent.CheckoutSessionId);
        Assert.Equal("https://www.asaas.com/comprovantes/abc", providerEvent.ReceiptUrl);
    }

    [Fact]
    public void ParseWebhookCarriesCheckoutSessionOfSubscriptionPayment()
    {
        var gateway = GatewayTestFactory.CreateAsaas(new RecordingHttpMessageHandler());
        const string payload = """
            {
              "id": "evt_card_created",
              "event": "PAYMENT_CREATED",
              "payment": {
                "id": "pay_4n81cieqp628gjft",
                "billingType": "CREDIT_CARD",
                "status": "PENDING",
                "subscription": "sub_tx3h4j0fd3z253xx",
                "checkoutSession": "afffcac7-dd82-4bd9-b994-48355b4eb8f3",
                "invoiceUrl": "https://www.asaas.com/i/4n81cieqp628gjft"
              }
            }
            """;

        var providerEvent = Assert.Single(gateway.ParseWebhook(payload).Batch.Events);

        Assert.Equal("afffcac7-dd82-4bd9-b994-48355b4eb8f3", providerEvent.CheckoutSessionId);
        Assert.Equal("https://www.asaas.com/i/4n81cieqp628gjft", providerEvent.ReceiptUrl);
    }

    [Fact]
    public void ParseWebhookRejectsPayloadWithoutEventIdentity()
    {
        var gateway = GatewayTestFactory.CreateAsaas(new RecordingHttpMessageHandler());

        Assert.Throws<FormatException>(() => gateway.ParseWebhook("{\"payment\":{}}"));
    }

    private static BankSlipGatewayContext CreateContext()
        => new()
        {
            TenantId = Guid.NewGuid(),
            Environment = BankSlipProviderEnvironment.Sandbox,
            CredentialReference = "asaas-sandbox"
        };
}
