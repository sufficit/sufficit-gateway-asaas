using Sufficit.Gateway;
using System.Text.Json;
using Xunit;

namespace Sufficit.Gateway.Asaas.Tests;

public sealed class AsaasGatewayWebhookSubscriptionTests
{
    private static readonly Uri CallbackUrl = new(
        "https://endpoints.sufficit.com.br/v2/Finance/BankSlip/ProviderNotification/Webhook?tenantId=095132cd-b1c4-4043-ae87-0a59cf2e0569&provider=asaas");

    [Fact]
    public async Task EnsureCreatesSequentialWebhookWithProtectedAuthenticationToken()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson("""{"object":"list","data":[],"hasMore":false}""");
        handler.EnqueueJson(WebhookJson("wh_created", enabled: true));
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.EnsureAsync(NewRequest(), Context(), CancellationToken.None);

        Assert.Equal(AsaasWebhookProvisioningOutcome.Created, result.Outcome);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.Equal("https://api-sandbox.asaas.com/v3/webhooks", handler.Requests[1].Uri.AbsoluteUri);
        using var body = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.Equal("SEQUENTIALLY", body.RootElement.GetProperty("sendType").GetString());
        Assert.Equal(3, body.RootElement.GetProperty("apiVersion").GetInt32());
        Assert.Equal(
            "0123456789abcdef0123456789abcdef",
            body.RootElement.GetProperty("authToken").GetString());
        Assert.Contains(
            body.RootElement.GetProperty("events").EnumerateArray(),
            item => item.GetString() == "PAYMENT_RECEIVED");
    }

    [Fact]
    public async Task EnsureDoesNotMutateMatchingWebhook()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson($$"""{"object":"list","data":[{{WebhookJson("wh_existing", enabled: true)}}],"hasMore":false}""");
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.EnsureAsync(NewRequest(), Context(), CancellationToken.None);

        Assert.Equal(AsaasWebhookProvisioningOutcome.Unchanged, result.Outcome);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task EnsureUpdatesInterruptedWebhookAndKeepsItsIdentifier()
    {
        var handler = new RecordingHttpMessageHandler();
        var interrupted = WebhookJson("wh_existing", enabled: true, interrupted: true);
        handler.EnqueueJson($$"""{"object":"list","data":[{{interrupted}}],"hasMore":false}""");
        handler.EnqueueJson(WebhookJson("wh_existing", enabled: true));
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.EnsureAsync(NewRequest(), Context(), CancellationToken.None);

        Assert.Equal(AsaasWebhookProvisioningOutcome.Updated, result.Outcome);
        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
        Assert.EndsWith("/v3/webhooks/wh_existing", handler.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListWebhooksIsAvailableInAdministratorDiagnosticCatalog()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson("""{"object":"list","data":[],"hasMore":false}""");
        IGatewayDiagnosticsGateway gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.ExecuteDiagnosticAsync(
            new GatewayDiagnosticRequest
            {
                Provider = "asaas",
                OperationCode = "webhooks.list",
                Offset = 0,
                Limit = 20
            },
            Context(),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.EndsWith("/v3/webhooks?offset=0&limit=20", handler.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    private static AsaasWebhookProvisioningRequest NewRequest()
        => new()
        {
            Name = "Sufficit BankSlip V2",
            Url = CallbackUrl,
            NotificationEmail = "infra@sufficit.com.br",
            Events = AsaasWebhookEventSets.BankSlipLifecycle
        };

    private static GatewayCallContext Context()
        => new()
        {
            TenantId = OSInformation.SufficitId,
            Environment = GatewayEnvironment.Sandbox,
            CredentialReference = "tests/asaas"
        };

    private static string WebhookJson(
        string id,
        bool enabled,
        bool interrupted = false)
        => JsonSerializer.Serialize(new
        {
            id,
            name = "Sufficit BankSlip V2",
            url = CallbackUrl.AbsoluteUri,
            email = "infra@sufficit.com.br",
            enabled,
            interrupted,
            sendType = "SEQUENTIALLY",
            events = AsaasWebhookEventSets.BankSlipLifecycle
        });
}
