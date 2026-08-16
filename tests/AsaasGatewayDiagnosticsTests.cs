using Sufficit.Gateway;
using Xunit;

namespace Sufficit.Gateway.Asaas.Tests;

public sealed class AsaasGatewayDiagnosticsTests
{
    [Fact]
    public async Task InvoiceListIsAllowListedAndUsesReadOnlyEndpoint()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson("""{"object":"list","data":[],"hasMore":false}""");
        IGatewayDiagnosticsGateway gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.ExecuteDiagnosticAsync(
            new GatewayDiagnosticRequest
            {
                Provider = "asaas",
                OperationCode = "invoices.list",
                Offset = 20,
                Limit = 10
            },
            CreateContext(),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(200, result.HttpStatusCode);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(
            "https://api-sandbox.asaas.com/v3/invoices?offset=20&limit=10",
            handler.Requests[0].Uri.AbsoluteUri);
        Assert.Contains(gateway.DiagnosticOperations, item =>
            item.Code == "credit-card.tokenize" && !item.Available);
    }

    private static GatewayCallContext CreateContext()
        => new()
        {
            TenantId = OSInformation.SufficitId,
            Environment = GatewayEnvironment.Sandbox,
            CredentialReference = "tests/asaas"
        };
}
