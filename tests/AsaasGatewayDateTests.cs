using Sufficit.Finance;
using Sufficit.Gateway;
using System.Globalization;
using System.Text.Json;
using Xunit;

namespace Sufficit.Gateway.Asaas.Tests;

public sealed class AsaasGatewayDateTests
{
    [Theory]
    [InlineData("2026-11-03 07:00:00", "2026-11-03T10:00:00Z")]
    [InlineData("2026-11-03T23:30:00.123", "2026-11-04T02:30:00.123Z")]
    [InlineData("2026-11-03", "2026-11-03T03:00:00Z")]
    [InlineData("2018-01-15 00:00:00", "2018-01-15T03:00:00Z")]
    [InlineData("2026-11-03T07:00", "2026-11-03T10:00:00Z")]
    [InlineData("2026-11-03T07:00:00Z", "2026-11-03T07:00:00Z")]
    [InlineData("2026-11-03T07:00:00-03:00", "2026-11-03T10:00:00Z")]
    [InlineData("2026-11-03T07:00:00+02:00", "2026-11-03T05:00:00Z")]
    [InlineData("2026-11-03T07:00:00+02", "2026-11-03T05:00:00Z")]
    [InlineData("2026-11-03T07:00:00+0200", "2026-11-03T05:00:00Z")]
    public void AllProviderDateReadersRespectExplicitOffsetOrDefaultToMinusThree(string input, string expected)
    {
        var gateway = GatewayTestFactory.CreateAsaas(new RecordingHttpMessageHandler());
        var utc = DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture);
        var payment = new {id="pay_date", billingType="BOLETO", status="RECEIVED", value=60,
            clientPaymentDate=input, externalReference="8c732677a5ea4f33a8e13dfcdb538411"};
        var bill = Assert.Single(gateway.ParseWebhook(JsonSerializer.Serialize(new {
            id="evt_date", @event="PAYMENT_RECEIVED", dateCreated=input, payment })).Batch.Events);
        Assert.Equal(utc.UtcDateTime, bill.EventAtUtc);
        Assert.Equal(utc.UtcDateTime, bill.PaidAtUtc);
        Assert.Equal(DateTimeKind.Utc, bill.PaidAtUtc!.Value.Kind);
        var pix = ((IPixPaymentGateway)gateway).ParseWebhook(JsonSerializer.Serialize(new {
            id="evt_date", @event="PAYMENT_RECEIVED", dateCreated=input,
            payment=new{id="pay_date",billingType="PIX",status="RECEIVED",value=60,paymentDate=input} }));
        Assert.Equal(utc, pix.EventAt);
        Assert.Equal(utc, pix.PaidAt);
        var checkout = gateway.ParseCheckoutWebhook(JsonSerializer.Serialize(new {
            id="evt_date", @event="CHECKOUT_PAID", dateCreated=input, checkout=new{id="chk_date",status="PAID"} }));
        Assert.Equal(utc, checkout.CreatedAt);
        var invoice = gateway.Parse(JsonSerializer.Serialize(new {
            id="evt_date", @event="INVOICE_AUTHORIZED",dateCreated=input,
            invoice=new{id="inv_date",status="AUTHORIZED",effectiveDate="2026-11-03"} }));
        Assert.Equal(utc, invoice.EventAtUtc);
        Assert.Equal(new DateOnly(2026, 11, 3), invoice.Invoice.EffectiveDate);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("2026-13-40 07:00:00")]
    [InlineData("2026-11-03T07:00:00+99:00")]
    public void MissingOrInvalidProviderDatesStayUnknown(string? input)
    {
        var gateway = GatewayTestFactory.CreateAsaas(new RecordingHttpMessageHandler());
        var checkout = gateway.ParseCheckoutWebhook(JsonSerializer.Serialize(new {
            id="evt_date", @event="CHECKOUT_PAID", dateCreated=input, checkout=new{id="chk_date",status="PAID"} }));
        Assert.Null(checkout.CreatedAt);
    }

}
