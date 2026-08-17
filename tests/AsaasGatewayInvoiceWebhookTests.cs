using Sufficit.Gateway;
using System.Net;
using System.Text;
using Xunit;

namespace Sufficit.Gateway.Asaas.Tests;

public sealed class AsaasGatewayInvoiceWebhookTests
{
    [Fact]
    public async Task AuthenticatesAndParsesAuthorizedInvoiceEvent()
    {
        var gateway = GatewayTestFactory.CreateAsaas(new RecordingHttpMessageHandler());
        var authenticated = await gateway.AuthenticateAsync(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ASAAS-ACCESS-TOKEN"] = "0123456789abcdef0123456789abcdef"
            },
            CreateContext(),
            CancellationToken.None);

        var envelope = gateway.Parse(
            """
            {
              "id": "evt_invoice_authorized_1",
              "event": "INVOICE_AUTHORIZED",
              "dateCreated": "2026-08-14 16:45:03",
              "invoice": {
                "id": "inv_000000000232",
                "status": "AUTHORIZED",
                "customer": "cus_000000002750",
                "value": 300,
                "pdfUrl": "https://api.notagateway.com.br/a/pdf",
                "xmlUrl": "https://api.notagateway.com.br/a/xml",
                "futureField": "preserved"
              }
            }
            """);

        Assert.True(authenticated);
        Assert.Equal("evt_invoice_authorized_1", envelope.EventId);
        Assert.Equal("INVOICE_AUTHORIZED", envelope.EventType);
        Assert.Equal("inv_000000000232", envelope.Invoice.Id);
        Assert.Equal("cus_000000002750", envelope.Invoice.CustomerId);
        Assert.Equal(300m, envelope.Invoice.Value);
        Assert.NotNull(envelope.Invoice.AdditionalProperties);
        Assert.True(envelope.Invoice.AdditionalProperties!.ContainsKey("futureField"));
    }

    [Fact]
    public async Task GetsCustomerThroughRateLimitedApiClient()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson("""{"id":"cus_test","cpfCnpj":"12345678000190","name":"Sufficit"}""");
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var customer = await gateway.GetCustomerAsync(
            "cus_test",
            CreateContext(),
            CancellationToken.None);

        Assert.Equal("12345678000190", customer?.Document);
        Assert.Equal("https://api-sandbox.asaas.com/v3/customers/cus_test", handler.Requests[0].Uri.AbsoluteUri);
        Assert.True(handler.Requests[0].Headers.ContainsKey("access_token"));
    }

    [Fact]
    public async Task DownloadsOnlyAllowlistedInvoiceDocumentsWithoutApiKey()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueResponse((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("invoice-pdf"))
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf") }
            }
        }));
        handler.EnqueueResponse((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("invoice-xml"))
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/xml") }
            }
        }));
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var document = await gateway.DownloadDocumentAsync(
            new Uri("https://api.notagateway.com.br/files/test/pdf"),
            CancellationToken.None);

        Assert.Equal("invoice-pdf", Encoding.UTF8.GetString(document.Content));
        Assert.Equal("application/pdf", document.ContentType);
        Assert.False(handler.Requests[0].Headers.ContainsKey("access_token"));

        var currentAsaasDocument = await gateway.DownloadDocumentAsync(
            new Uri("https://www.asaas.com/files/test/xml"),
            CancellationToken.None);

        Assert.Equal("invoice-xml", Encoding.UTF8.GetString(currentAsaasDocument.Content));
        Assert.Equal("application/xml", currentAsaasDocument.ContentType);
        Assert.False(handler.Requests[1].Headers.ContainsKey("access_token"));
        await Assert.ThrowsAsync<ArgumentException>(() => gateway.DownloadDocumentAsync(
            new Uri("https://127.0.0.1/private"),
            CancellationToken.None));
    }

    [Fact]
    public void RejectsPayloadWithoutInvoiceIdentity()
    {
        var gateway = GatewayTestFactory.CreateAsaas(new RecordingHttpMessageHandler());
        Assert.Throws<FormatException>(() => gateway.Parse(
            """{"id":"evt_1","event":"INVOICE_AUTHORIZED","invoice":{}}"""));
    }

    private static GatewayCallContext CreateContext() => new()
    {
        TenantId = OSInformation.SufficitId,
        Environment = GatewayEnvironment.Sandbox,
        CredentialReference = "tests/asaas"
    };
}
