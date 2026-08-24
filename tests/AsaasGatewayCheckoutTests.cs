using Sufficit.Gateway;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Sufficit.Gateway.Asaas.Tests;

public sealed class AsaasGatewayCheckoutTests
{
    [Fact]
    public async Task GetAccountReadsBeneficiaryIdentityWithoutMutation()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson(
            """
            {
              "object": "commercialInfo",
              "personType": "JURIDICA",
              "cpfCnpj": "13.151.997/0001-22",
              "companyName": "SUFFICIT SOLUCOES EM TECNOLOGIA DA INFORMACAO LTDA"
            }
            """);
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var account = await gateway.GetAccountAsync(CreateContext(), CancellationToken.None);

        Assert.Equal("13151997000122", account.Document);
        Assert.Equal("JURIDICA", account.PersonType);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api-sandbox.asaas.com/v3/myAccount/commercialInfo/", request.Uri.AbsoluteUri);
        Assert.Null(request.Body);
    }

    [Fact]
    public async Task GetAccountAcceptsApprovedSandboxAccountWithoutDocument()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson(
            """{"object":"commercialInfo","cpfCnpj":null,"personType":null,"status":"APPROVED"}""");
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var account = await gateway.GetAccountAsync(CreateContext(), CancellationToken.None);

        Assert.Empty(account.Document);
        Assert.Equal("APPROVED", account.Status);
    }

    [Fact]
    public async Task GetAccountRejectsProductionAccountWithoutDocument()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson(
            """{"object":"commercialInfo","cpfCnpj":null,"personType":null,"status":"APPROVED"}""");
        var gateway = GatewayTestFactory.CreateAsaas(handler);
        var context = CreateContext();
        context.Environment = GatewayEnvironment.Production;

        var exception = await Assert.ThrowsAsync<AsaasGatewayException>(() => gateway.GetAccountAsync(
            context,
            CancellationToken.None));

        Assert.Equal("asaas_account_invalid_response", exception.ErrorCode);
    }

    [Fact]
    public async Task CreateCheckoutUsesHostedProviderContract()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson(
            """
            {
              "id": "chk_test",
              "link": "https://sandbox.asaas.com/checkoutSession/show/chk_test",
              "status": "ACTIVE",
              "externalReference": "checkout-session-id"
            }
            """);
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.CreateAsync(
            new AsaasCheckoutRequest
            {
                BillingTypes = [AsaasCheckoutBillingType.Pix],
                MinutesToExpire = 30,
                ExternalReference = "checkout-session-id",
                SuccessUrl = new Uri("https://checkout.sufficit.com.br/provider-return/success"),
                CancelUrl = new Uri("https://checkout.sufficit.com.br/provider-return/cancel"),
                ExpiredUrl = new Uri("https://checkout.sufficit.com.br/provider-return/expired"),
                Items =
                [
                    new AsaasCheckoutItem
                    {
                        ExternalReference = "mobile-small",
                        Name = "Sufficit Mobile Small",
                        Description = "Mensalidade",
                        Quantity = 1,
                        Value = 159.90m
                    }
                ]
            },
            CreateContext(),
            CancellationToken.None);

        Assert.Equal("chk_test", result.Id);
        Assert.Equal("https://sandbox.asaas.com/checkoutSession/show/chk_test", result.Link.AbsoluteUri);
        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, recorded.Method);
        Assert.Equal("https://api-sandbox.asaas.com/v3/checkouts", recorded.Uri.AbsoluteUri);
        Assert.Equal("$aact_hmlg_test", recorded.Headers["access_token"].Single());

        using var payload = JsonDocument.Parse(recorded.Body!);
        Assert.Equal("PIX", payload.RootElement.GetProperty("billingTypes")[0].GetString());
        Assert.Equal("DETACHED", payload.RootElement.GetProperty("chargeTypes")[0].GetString());
        Assert.Equal("checkout-session-id", payload.RootElement.GetProperty("externalReference").GetString());
        Assert.Equal(159.90m, payload.RootElement.GetProperty("items")[0].GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task CheckoutWebhookAuthenticationUsesProtectedCredential()
    {
        var gateway = GatewayTestFactory.CreateAsaas(new RecordingHttpMessageHandler());

        var accepted = await gateway.AuthenticateWebhookAsync(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ASAAS-ACCESS-TOKEN"] = "0123456789abcdef0123456789abcdef"
            },
            CreateContext(),
            CancellationToken.None);
        var rejected = await gateway.AuthenticateWebhookAsync(
            new Dictionary<string, string>
            {
                [AsaasGateway.WebhookAuthenticationHeader] = "not-the-secret"
            },
            CreateContext(),
            CancellationToken.None);

        Assert.True(accepted);
        Assert.False(rejected);
    }

    [Fact]
    public void ParseCheckoutWebhookPreservesIdentityAndIgnoresUnknownFields()
    {
        var gateway = GatewayTestFactory.CreateAsaas(new RecordingHttpMessageHandler());

        var result = gateway.ParseCheckoutWebhook(
            """
            {
              "id": "evt_checkout_paid_1",
              "event": "CHECKOUT_PAID",
              "dateCreated": "2026-08-24 12:15:00",
              "futureField": true,
              "checkout": {
                "id": "chk_test",
                "status": "PAID",
                "externalReference": "checkout-session-id",
                "anotherFutureField": { "enabled": true }
              }
            }
            """);

        Assert.Equal("evt_checkout_paid_1", result.EventId);
        Assert.Equal("CHECKOUT_PAID", result.EventType);
        Assert.Equal("chk_test", result.CheckoutId);
        Assert.Equal("PAID", result.Status);
        Assert.Equal("checkout-session-id", result.ExternalReference);
        Assert.NotNull(result.CreatedAt);
    }

    [Fact]
    public async Task InvalidProviderLinkFallsBackToTrustedAsaasHost()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson("""{"id":"chk_safe","link":"https://attacker.example/steal"}""");
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var result = await gateway.CreateAsync(CreateValidRequest(), CreateContext(), CancellationToken.None);

        Assert.Equal("https://sandbox.asaas.com/checkoutSession/show?id=chk_safe", result.Link.AbsoluteUri);
    }

    [Fact]
    public async Task InconsistentExternalReferenceIsRejected()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson(
            """{"id":"chk_wrong","status":"ACTIVE","externalReference":"another-order"}""");
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var exception = await Assert.ThrowsAsync<AsaasGatewayException>(() => gateway.CreateAsync(
            CreateValidRequest(),
            CreateContext(),
            CancellationToken.None));

        Assert.Equal("asaas_checkout_invalid_response", exception.ErrorCode);
    }

    [Fact]
    public async Task FailedCreationMapsProviderErrorWithoutPayloadLeak()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJson(
            """{"errors":[{"code":"invalid_request","description":"sensitive provider detail"}]}""",
            HttpStatusCode.BadRequest);
        var gateway = GatewayTestFactory.CreateAsaas(handler);

        var exception = await Assert.ThrowsAsync<AsaasGatewayException>(() => gateway.CreateAsync(
            CreateValidRequest(),
            CreateContext(),
            CancellationToken.None));

        Assert.Equal("invalid_request", exception.ErrorCode);
        Assert.DoesNotContain("sensitive", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static AsaasCheckoutRequest CreateValidRequest() => new()
    {
        BillingTypes = [AsaasCheckoutBillingType.CreditCard],
        MinutesToExpire = 60,
        ExternalReference = "checkout-session-id",
        SuccessUrl = new Uri("https://checkout.sufficit.com.br/success"),
        CancelUrl = new Uri("https://checkout.sufficit.com.br/cancel"),
        ExpiredUrl = new Uri("https://checkout.sufficit.com.br/expired"),
        Items = [new AsaasCheckoutItem { Name = "Plano", Quantity = 1, Value = 159.90m }]
    };

    private static GatewayCallContext CreateContext() => new()
    {
        TenantId = OSInformation.SufficitId,
        Environment = GatewayEnvironment.Sandbox,
        CredentialReference = "tests/asaas"
    };
}
