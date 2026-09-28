using Sufficit.Gateway;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Sufficit.Gateway.Asaas.Tests;

public sealed class AsaasGatewayNativePaymentTests
{
    [Fact]
    public async Task PayExistingChargePostsCardOnlyToPayEndpoint()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson("""{"id":"pay_test","billingType":"CREDIT_CARD","status":"CONFIRMED","value":5.00,"externalReference":"checkout:test"}""");
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.PayWithCreditCardAsync("pay_test", new AsaasCreditCardPaymentRequest
        {
            CreditCard = new AsaasCreditCard
            {
                HolderName = "Cliente Teste", Number = "4111111111111111",
                ExpiryMonth = "12", ExpiryYear = "2030", Ccv = "123"
            },
            CreditCardHolderInfo = new AsaasCreditCardHolderInfo
            {
                Name = "Cliente Teste", Email = "teste@example.invalid",
                Document = "12345678909", PostalCode = "12345678",
                AddressNumber = "12", Phone = "11999999999"
            }
        }, CreateContext(), CancellationToken.None);

        Assert.Equal("CONFIRMED", result.Status);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://api-sandbox.asaas.com/v3/payments/pay_test/payWithCreditCard", sent.Uri.AbsoluteUri);
        using var body = JsonDocument.Parse(sent.Body!);
        Assert.Equal("4111111111111111", body.RootElement.GetProperty("creditCard").GetProperty("number").GetString());
        Assert.Equal("12345678909", body.RootElement.GetProperty("creditCardHolderInfo").GetProperty("cpfCnpj").GetString());
    }

    [Fact]
    public async Task CreatePaymentPostsNativeContract()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson(
            """
            {
              "id": "pay_001",
              "customer": "cus_001",
              "value": 159.90,
              "netValue": 152.55,
              "billingType": "PIX",
              "status": "PENDING",
              "dueDate": "2026-10-01",
              "description": "Mensalidade Sufficit",
              "externalReference": "checkout:session-1",
              "invoiceUrl": "https://sandbox.asaas.com/i/pay_001",
              "dateCreated": "2026-09-23 10:00:00"
            }
            """);
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.CreatePaymentAsync(
            new AsaasPaymentCreateRequest
            {
                CustomerId = "cus_001",
                BillingType = AsaasPaymentBillingTypes.Pix,
                Value = 159.90m,
                DueDate = new DateOnly(2026, 10, 1),
                Description = "Mensalidade Sufficit",
                ExternalReference = "checkout:session-1"
            },
            CreateContext(),
            CancellationToken.None);

        Assert.Equal("pay_001", result.Id);
        Assert.Equal("cus_001", result.CustomerId);
        Assert.Equal(159.90m, result.Value);
        Assert.Equal(152.55m, result.NetValue);
        Assert.Equal("PIX", result.BillingType);
        Assert.Equal("PENDING", result.Status);
        Assert.Equal(new DateOnly(2026, 10, 1), result.DueDate);
        Assert.Equal("https://sandbox.asaas.com/i/pay_001", result.InvoiceUrl!.AbsoluteUri);
        Assert.Equal("2026-09-23 10:00:00", result.DateCreated);

        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, recorded.Method);
        Assert.Equal("https://api-sandbox.asaas.com/v3/payments", recorded.Uri.AbsoluteUri);
        Assert.Equal("$aact_hmlg_test", recorded.Headers["access_token"].Single());
        using var payload = JsonDocument.Parse(recorded.Body!);
        Assert.Equal("cus_001", payload.RootElement.GetProperty("customer").GetString());
        Assert.Equal("PIX", payload.RootElement.GetProperty("billingType").GetString());
        Assert.Equal(159.90m, payload.RootElement.GetProperty("value").GetDecimal());
        Assert.Equal("2026-10-01", payload.RootElement.GetProperty("dueDate").GetString());
        Assert.Equal("checkout:session-1", payload.RootElement.GetProperty("externalReference").GetString());
    }

    [Fact]
    public async Task CreateCardPaymentSendsHostedReturnCallback()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson("""{"id":"pay_card_1","billingType":"CREDIT_CARD","status":"PENDING"}""");
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        await gateway.CreatePaymentAsync(new AsaasPaymentCreateRequest
        {
            CustomerId = "cus_001",
            BillingType = AsaasPaymentBillingTypes.CreditCard,
            Value = 5m,
            DueDate = new DateOnly(2026, 10, 1),
            Callback = new AsaasPaymentCallback
            {
                SuccessUrl = new Uri("https://checkout.sufficit.com.br/c/scm1.payload.signature"),
                AutoRedirect = true
            }
        }, CreateContext(), CancellationToken.None);

        using var payload = JsonDocument.Parse(Assert.Single(handler.Requests).Body!);
        var callback = payload.RootElement.GetProperty("callback");
        Assert.Equal("https://checkout.sufficit.com.br/c/scm1.payload.signature",
            callback.GetProperty("successUrl").GetString());
        Assert.True(callback.GetProperty("autoRedirect").GetBoolean());
    }

    [Fact]
    public async Task CreatePaymentRejectsUnknownBillingType()
    {
        var gateway = GatewayTestFactory.CreateAsaas(new RecordingHttpMessageHandler());

        await Assert.ThrowsAsync<ArgumentException>(() => gateway.CreatePaymentAsync(
            new AsaasPaymentCreateRequest
            {
                CustomerId = "cus_001",
                BillingType = "CARRIER_PIGEON",
                Value = 10m,
                DueDate = new DateOnly(2026, 10, 1)
            },
            CreateContext(),
            CancellationToken.None));
    }

    [Fact]
    public async Task CreatePaymentRequiresPositiveValue()
    {
        var gateway = GatewayTestFactory.CreateAsaas(new RecordingHttpMessageHandler());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => gateway.CreatePaymentAsync(
            new AsaasPaymentCreateRequest
            {
                CustomerId = "cus_001",
                BillingType = AsaasPaymentBillingTypes.Boleto,
                Value = 0m,
                DueDate = new DateOnly(2026, 10, 1)
            },
            CreateContext(),
            CancellationToken.None));
    }

    [Fact]
    public async Task GetPaymentReturnsNullWhenMissing()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson("{}", HttpStatusCode.NotFound);
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.GetPaymentAsync("pay_missing", CreateContext(), CancellationToken.None);

        Assert.Null(result);
        var recorded = Assert.Single(handler.Requests);
        Assert.Equal("https://api-sandbox.asaas.com/v3/payments/pay_missing", recorded.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task GetPaymentStatusReadsStatusEndpoint()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson("""{"id":"pay_001","status":"RECEIVED"}""");
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.GetPaymentStatusAsync("pay_001", CreateContext(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("RECEIVED", result!.Status);
        var recorded = Assert.Single(handler.Requests);
        Assert.Equal("https://api-sandbox.asaas.com/v3/payments/pay_001/status", recorded.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task GetIdentificationFieldReadsBankLine()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson(
            """
            {
              "identificationField": "00000000000000000000000000000000000000000000000",
              "barCode": "0000000000000000000000000000000000000"
            }
            """);
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.GetIdentificationFieldAsync("pay_001", CreateContext(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("00000000000000000000000000000000000000000000000", result!.IdentificationField);
        Assert.NotNull(result.BarCode);
        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://api-sandbox.asaas.com/v3/payments/pay_001/identificationField",
            recorded.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task GetPixQrCodeReadsPayloadAndImage()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson(
            """
            {
              "payload": "00020126580014BR.GOV.BCB.PIX0136a-fake-pix-key",
              "encodedImage": "data:image/png;base64,iVBORw0KGgo=",
              "expirationDate": "2026-09-23 11:00:00"
            }
            """);
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.GetPixQrCodeAsync("pay_001", CreateContext(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("00020126580014BR.GOV.BCB.PIX0136a-fake-pix-key", result!.Payload);
        Assert.Equal("data:image/png;base64,iVBORw0KGgo=", result.EncodedImage);
        Assert.Equal("2026-09-23 11:00:00", result.ExpirationDate);
        var recorded = Assert.Single(handler.Requests);
        Assert.Equal("https://api-sandbox.asaas.com/v3/payments/pay_001/pixQrCode", recorded.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task DeletePaymentConfirmsDeletion()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson("""{"id":"pay_001","deleted":true}""");
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.DeletePaymentAsync("pay_001", CreateContext(), CancellationToken.None);

        Assert.True(result.Deleted);
        Assert.Equal("pay_001", result.Id);
        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, recorded.Method);
        Assert.Equal("https://api-sandbox.asaas.com/v3/payments/pay_001", recorded.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task ListPaymentsBuildsFilteredQuery()
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
              "data": [ { "id": "pay_001", "billingType": "BOLETO", "status": "PENDING" } ]
            }
            """);
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.ListPaymentsAsync(
            new AsaasPaymentSearchParameters
            {
                CustomerId = "cus_001",
                ExternalReference = "checkout:session-1"
            },
            CreateContext(),
            CancellationToken.None);

        Assert.Single(result.Data);
        Assert.Equal("BOLETO", result.Data[0].BillingType);
        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://api-sandbox.asaas.com/v3/payments?offset=0&limit=20&customer=cus_001&externalReference=checkout%3Asession-1",
            recorded.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task ListPaymentsRejectsInvalidPaging()
    {
        var gateway = GatewayTestFactory.CreateAsaas(new RecordingHttpMessageHandler());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => gateway.ListPaymentsAsync(
            new AsaasPaymentSearchParameters { Limit = 0 },
            CreateContext(),
            CancellationToken.None));
    }

    [Fact]
    public async Task PaymentOperationsRequireContext()
    {
        var gateway = GatewayTestFactory.CreateAsaas(new RecordingHttpMessageHandler());

        await Assert.ThrowsAsync<ArgumentNullException>(() => gateway.GetPaymentAsync(
            "pay_001",
            null!,
            CancellationToken.None));
    }

    private static GatewayCallContext CreateContext() => new()
    {
        TenantId = Guid.NewGuid(),
        Environment = GatewayEnvironment.Sandbox,
        CredentialReference = "tests/asaas"
    };
}
