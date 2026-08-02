using Sufficit.Gateway;
using System.Text.Json;
using Xunit;

namespace Sufficit.Gateway.Asaas.Tests;

public class AsaasGatewayInvoiceTests
{
    [Fact]
    public async Task ListInvoicesUsesTypedFiltersAndGenericGatewayContext()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson(
            """
            {
              "object": "list",
              "hasMore": false,
              "totalCount": 1,
              "limit": 25,
              "offset": 0,
              "data": [
                {
                  "id": "inv_test",
                  "status": "AUTHORIZED",
                  "customer": "cus_test",
                  "externalReference": "nf-123",
                  "value": 500.00,
                  "effectiveDate": "2026-07-30",
                  "pdfUrl": "https://sandbox.asaas.example/invoice.pdf"
                }
              ]
            }
            """);
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.ListInvoicesAsync(
            new AsaasInvoiceSearchParameters
            {
                Limit = 25,
                EffectiveDateFrom = new DateOnly(2026, 7, 1),
                EffectiveDateTo = new DateOnly(2026, 7, 31),
                CustomerId = "cus_test",
                Status = "AUTHORIZED"
            },
            CreateContext(),
            CancellationToken.None);

        var invoice = Assert.Single(result.Data);
        Assert.Equal("inv_test", invoice.Id);
        Assert.Equal("AUTHORIZED", invoice.Status);
        Assert.Contains("limit=25", handler.Requests[0].Uri.Query);
        Assert.Contains("effectiveDate%5Bge%5D=2026-07-01", handler.Requests[0].Uri.Query);
        Assert.Contains("customer=cus_test", handler.Requests[0].Uri.Query);
        Assert.Equal("$aact_hmlg_test", handler.Requests[0].Headers["access_token"].Single());
    }

    [Fact]
    public async Task ScheduleInvoiceUsesProviderLevelConfigurationAndTypedPayload()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson(
            """
            {
              "id": "inv_test",
              "status": "SCHEDULED",
              "customer": "cus_test",
              "externalReference": "nf-123",
              "value": 500.00,
              "effectiveDate": "2026-08-03"
            }
            """);
        var gateway = GatewayTestFactory.CreateAsaas(handler);
        using var taxesDocument = JsonDocument.Parse(
            """{"retainIss":false,"iss":2.0,"cofins":0.0,"csll":0.0,"inss":0.0,"ir":0.0,"pis":0.0}""");

        var invoice = await gateway.ScheduleInvoiceAsync(
            new AsaasInvoiceScheduleRequest
            {
                CustomerId = "cus_test",
                ServiceDescription = "Serviços de software",
                Observations = "Competência julho de 2026",
                ExternalReference = "nf-123",
                Value = 500m,
                EffectiveDate = new DateOnly(2026, 8, 3),
                MunicipalServiceCode = "1.01",
                MunicipalServiceName = "Análise e desenvolvimento de sistemas",
                Taxes = taxesDocument.RootElement.Clone(),
                UseTaxSystemReformNT007 = true
            },
            CreateContext(),
            CancellationToken.None);

        Assert.Equal("inv_test", invoice.Id);
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("https://api-sandbox.asaas.com/v3/invoices", handler.Requests[0].Uri.AbsoluteUri);
        Assert.Contains("\"customer\":\"cus_test\"", handler.Requests[0].Body);
        Assert.Contains("\"municipalServiceCode\":\"1.01\"", handler.Requests[0].Body);
        Assert.Contains("\"useTaxSystemReformNT007\":true", handler.Requests[0].Body);
        Assert.DoesNotContain("\"payment\"", handler.Requests[0].Body);
    }

    [Fact]
    public async Task InvoiceLifecycleUsesTheTypedProviderOperations()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson("""{"errors":[{"code":"not_found"}]}""", System.Net.HttpStatusCode.NotFound);
        handler.EnqueueJson("""{"id":"inv_test","status":"SCHEDULED","value":550.00}""");
        handler.EnqueueJson("""{"id":"inv_test","status":"SYNCHRONIZED","value":550.00}""");
        handler.EnqueueJson("""{"id":"inv_test","status":"PROCESSING_CANCELLATION","value":550.00}""");
        var gateway = GatewayTestFactory.CreateAsaas(handler);
        var context = CreateContext();

        var missing = await gateway.GetInvoiceAsync(
            "inv_missing",
            context,
            CancellationToken.None);
        var updated = await gateway.UpdateInvoiceAsync(
            "inv_test",
            new AsaasInvoiceUpdateRequest
            {
                Value = 550m,
                Observations = "Valor corrigido"
            },
            context,
            CancellationToken.None);
        var authorized = await gateway.AuthorizeInvoiceAsync(
            "inv_test",
            context,
            CancellationToken.None);
        var canceled = await gateway.CancelInvoiceAsync(
            "inv_test",
            new AsaasInvoiceCancelRequest { CancelOnlyOnAsaas = false },
            context,
            CancellationToken.None);

        Assert.Null(missing);
        Assert.Equal("SCHEDULED", updated.Status);
        Assert.Equal("SYNCHRONIZED", authorized.Status);
        Assert.Equal("PROCESSING_CANCELLATION", canceled.Status);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal("https://api-sandbox.asaas.com/v3/invoices/inv_missing", handler.Requests[0].Uri.AbsoluteUri);
        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
        Assert.Equal("https://api-sandbox.asaas.com/v3/invoices/inv_test", handler.Requests[1].Uri.AbsoluteUri);
        Assert.Contains("\"value\":550", handler.Requests[1].Body);
        Assert.Equal(HttpMethod.Post, handler.Requests[2].Method);
        Assert.Equal("https://api-sandbox.asaas.com/v3/invoices/inv_test/authorize", handler.Requests[2].Uri.AbsoluteUri);
        Assert.Null(handler.Requests[2].Body);
        Assert.Equal(HttpMethod.Post, handler.Requests[3].Method);
        Assert.Equal("https://api-sandbox.asaas.com/v3/invoices/inv_test/cancel", handler.Requests[3].Uri.AbsoluteUri);
        Assert.Contains("\"cancelOnlyOnAsaas\":false", handler.Requests[3].Body);
    }

    private static GatewayCallContext CreateContext()
        => new()
        {
            TenantId = OSInformation.SufficitId,
            Environment = GatewayEnvironment.Sandbox,
            CredentialReference = "tests/asaas"
        };
}
