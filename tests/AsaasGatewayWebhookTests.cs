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
