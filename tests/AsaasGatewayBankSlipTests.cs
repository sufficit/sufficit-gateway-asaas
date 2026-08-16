using Sufficit.Finance;
using Xunit;

namespace Sufficit.Gateway.Asaas.Tests;

public class AsaasGatewayBankSlipTests
{
    [Fact]
    public async Task CreateAsyncPreventsDuplicatesAndUsesSandboxBoletoFlow()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson("""{"object":"list","hasMore":false,"data":[]}""");
        handler.EnqueueJson("""{"object":"list","hasMore":false,"data":[]}""");
        handler.EnqueueJson("""{"object":"customer","id":"cus_test","cpfCnpj":"12345678000190"}""");
        handler.EnqueueJson(
            """{"object":"payment","id":"pay_test","customer":"cus_test","status":"PENDING","externalReference":"8c732677a5ea4f33a8e13dfcdb538411","invoiceUrl":"https://sandbox.asaas.example/i/pay_test","bankSlipUrl":"https://sandbox.asaas.example/b/pay_test.pdf"}""");
        handler.EnqueueJson("""{"identificationField":"0019000009","barCode":"0019000009"}""");
        var gateway = GatewayTestFactory.CreateAsaas(handler);
        var request = CreateIssueRequest();

        var result = await gateway.CreateAsync(request, CreateContext(), CancellationToken.None);

        Assert.Equal(BankSlipStatus.Ready, result.Status);
        Assert.Equal("pay_test", result.ChargeId);
        Assert.Equal("0019000009", result.BarCode);
        Assert.Equal("https://sandbox.asaas.example/i/pay_test", result.HtmlUrl?.AbsoluteUri);
        Assert.Equal("https://sandbox.asaas.example/b/pay_test.pdf", result.PdfUrl?.AbsoluteUri);
        Assert.Equal(result.PdfUrl, result.Url);
        Assert.Equal(5, handler.Requests.Count);
        Assert.StartsWith("https://api-sandbox.asaas.com/v3/payments?externalReference=", handler.Requests[0].Uri.AbsoluteUri);
        Assert.StartsWith("https://api-sandbox.asaas.com/v3/customers?cpfCnpj=", handler.Requests[1].Uri.AbsoluteUri);
        Assert.Equal("https://api-sandbox.asaas.com/v3/customers", handler.Requests[2].Uri.AbsoluteUri);
        Assert.Equal("https://api-sandbox.asaas.com/v3/payments", handler.Requests[3].Uri.AbsoluteUri);
        Assert.Contains("\"billingType\":\"BOLETO\"", handler.Requests[3].Body);
        Assert.Contains("\"externalReference\":\"8c732677a5ea4f33a8e13dfcdb538411\"", handler.Requests[3].Body);
        Assert.All(handler.Requests, recorded =>
        {
            Assert.Equal("$aact_hmlg_test", recorded.Headers["access_token"].Single());
            Assert.Equal("Sufficit-Gateway-Asaas.Tests/1.0", recorded.Headers["User-Agent"].Single());
        });
    }

    [Fact]
    public async Task CreateAsyncReusesPaymentFoundByExternalReference()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson(
            """{"object":"list","hasMore":false,"data":[{"id":"pay_existing","customer":"cus_existing","status":"PENDING","externalReference":"8c732677a5ea4f33a8e13dfcdb538411","bankSlipUrl":"https://sandbox.asaas.example/b/pay_existing"}]}""");
        handler.EnqueueJson("""{"identificationField":"3419100000"}""");
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.CreateAsync(CreateIssueRequest(), CreateContext(), CancellationToken.None);

        Assert.Equal("pay_existing", result.ChargeId);
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task CancelAsyncDeletesOriginalAsaasPayment()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson(
            """{"object":"payment","id":"pay_test","customer":"cus_test","status":"PENDING","externalReference":"8c732677a5ea4f33a8e13dfcdb538411","bankSlipUrl":"https://sandbox.asaas.example/b/pay_test"}""");
        handler.EnqueueJson("""{"identificationField":"3419100000"}""");
        handler.EnqueueJson("""{"deleted":true,"id":"pay_test"}""");
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.CancelAsync(
            "pay_test",
            CreateContext(),
            CancellationToken.None);

        Assert.True(result.Canceled);
        Assert.Equal("pay_test", result.ChargeId);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(HttpMethod.Delete, handler.Requests[2].Method);
        Assert.Equal(
            "https://api-sandbox.asaas.com/v3/payments/pay_test",
            handler.Requests[2].Uri.AbsoluteUri);
    }

    [Fact]
    public async Task DiagnosticAuthenticationUsesReadOnlyAccountEndpoint()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson(
            """{"personType":"JURIDICA","cpfCnpj":"12345678000190","email":"financeiro@example.test"}""");
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.ExecuteDiagnosticAsync(
            new BankSlipProviderDiagnosticParameters
            {
                Provider = BankSlipProviderCodes.Asaas,
                Operation = BankSlipProviderDiagnosticOperation.Authentication
            },
            CreateContext(),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(200, result.HttpStatusCode);
        Assert.Equal("JURIDICA", result.Payload.GetProperty("personType").GetString());
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(
            "https://api-sandbox.asaas.com/v3/myAccount/commercialInfo/",
            handler.Requests[0].Uri.AbsoluteUri);
    }

    [Fact]
    public async Task DiagnosticChargeReturnsNoResultForProviderNotFound()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson("""{"errors":[{"code":"not_found"}]}""", System.Net.HttpStatusCode.NotFound);
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.ExecuteDiagnosticAsync(
            new BankSlipProviderDiagnosticParameters
            {
                Provider = BankSlipProviderCodes.Asaas,
                Operation = BankSlipProviderDiagnosticOperation.Charge,
                ProviderChargeId = "pay_missing"
            },
            CreateContext(),
            CancellationToken.None);

        Assert.Null(result);
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }

    private static BankSlipGatewayIssueRequest CreateIssueRequest()
        => new()
        {
            BankSlipId = Guid.Parse("8c732677-a5ea-4f33-a8e1-3dfcdb538411"),
            ContextId = Guid.Parse("d9f76c63-e026-489b-9fd2-e3f5210dd8ac"),
            Value = 500m,
            Expiration = new DateTime(2026, 8, 10),
            Description = "Serviço Sufficit",
            Payer = new BankSlipPayerSnapshot
            {
                Document = "12.345.678/0001-90",
                Name = "Sufficit Cliente Ltda",
                Email = "financeiro@example.test",
                Phone = "31999999999",
                Address = new BankSlipPayerAddress
                {
                    Street = "Rua de Teste",
                    Number = "100",
                    Neighborhood = "Centro",
                    PostalCode = "30100-000",
                    City = "Belo Horizonte",
                    State = "MG"
                }
            }
        };

    private static BankSlipGatewayContext CreateContext()
        => new()
        {
            TenantId = OSInformation.SufficitId,
            Environment = BankSlipProviderEnvironment.Sandbox,
            CredentialReference = "tests/asaas"
        };
}
