using Sufficit.Gateway;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Sufficit.Gateway.Asaas.Tests;

public sealed class AsaasGatewayNativeCustomerTests
{
    [Fact]
    public async Task CreateCustomerPostsNativePayload()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson(
            """
            {
              "id": "cus_002",
              "name": "Maria Example",
              "cpfCnpj": "57522111000"
            }
            """);
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.CreateCustomerAsync(
            new AsaasCustomerCreateRequest
            {
                Name = "Maria Example",
                CpfCnpj = "575.221.110-00",
                Email = "maria@example.com",
                MobilePhone = "47988887777",
                ExternalReference = "checkout-customer:ctx-1",
                NotificationDisabled = true
            },
            CreateContext(),
            CancellationToken.None);

        Assert.Equal("cus_002", result.Id);
        Assert.Equal("Maria Example", result.Name);
        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, recorded.Method);
        Assert.Equal("https://api-sandbox.asaas.com/v3/customers", recorded.Uri.AbsoluteUri);
        using var payload = JsonDocument.Parse(recorded.Body!);
        Assert.Equal("Maria Example", payload.RootElement.GetProperty("name").GetString());
        Assert.Equal("575.221.110-00", payload.RootElement.GetProperty("cpfCnpj").GetString());
        Assert.Equal("checkout-customer:ctx-1", payload.RootElement.GetProperty("externalReference").GetString());
        Assert.True(payload.RootElement.GetProperty("notificationDisabled").GetBoolean());
    }

    [Fact]
    public async Task CreateCustomerRequiresName()
    {
        var gateway = GatewayTestFactory.CreateAsaas(new RecordingHttpMessageHandler());

        await Assert.ThrowsAsync<ArgumentException>(() => gateway.CreateCustomerAsync(
            new AsaasCustomerCreateRequest { CpfCnpj = "57522111000" },
            CreateContext(),
            CancellationToken.None));
    }

    [Fact]
    public async Task UpdateCustomerUsesPutAndEscapedIdentifier()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson("""{"id":"cus_001","name":"Maria Updated"}""");
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.UpdateCustomerAsync(
            "cus_001",
            new AsaasCustomerUpdateRequest
            {
                Name = "Maria Updated",
                Email = "maria.nova@example.com"
            },
            CreateContext(),
            CancellationToken.None);

        Assert.Equal("Maria Updated", result.Name);
        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, recorded.Method);
        Assert.Equal("https://api-sandbox.asaas.com/v3/customers/cus_001", recorded.Uri.AbsoluteUri);
        using var payload = JsonDocument.Parse(recorded.Body!);
        Assert.Equal("Maria Updated", payload.RootElement.GetProperty("name").GetString());
        Assert.Equal("maria.nova@example.com", payload.RootElement.GetProperty("email").GetString());
    }

    [Fact]
    public async Task ListCustomersBuildsFilteredQuery()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson(
            """
            {
              "object": "list",
              "hasMore": false,
              "totalCount": 1,
              "limit": 20,
              "offset": 0,
              "data": [ { "id": "cus_001", "name": "Maria Example", "cpfCnpj": "57522111000" } ]
            }
            """);
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.ListCustomersAsync(
            new AsaasCustomerSearchParameters { CpfCnpj = "57522111000" },
            CreateContext(),
            CancellationToken.None);

        var customer = Assert.Single(result.Data);
        Assert.Equal("cus_001", customer.Id);
        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://api-sandbox.asaas.com/v3/customers?offset=0&limit=20&cpfCnpj=57522111000",
            recorded.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task GetCustomerThroughNativeContractReturnsNullWhenMissing()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson("{}", HttpStatusCode.NotFound);
        var gateway = GatewayTestFactory.CreateAsaas(handler);
        IAsaasCustomerGateway typed = gateway;

        var result = await typed.GetCustomerAsync("cus_missing", CreateContext(), CancellationToken.None);

        Assert.Null(result);
        var recorded = Assert.Single(handler.Requests);
        Assert.Equal("https://api-sandbox.asaas.com/v3/customers/cus_missing", recorded.Uri.AbsoluteUri);
    }

    private static GatewayCallContext CreateContext() => new()
    {
        TenantId = Guid.NewGuid(),
        Environment = GatewayEnvironment.Sandbox,
        CredentialReference = "tests/asaas"
    };
}
