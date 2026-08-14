using Sufficit.Finance;
using Sufficit.Gateway;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sufficit.Gateway.Asaas;

/// <summary>
/// Inbound webhook capability for Asaas. It owns the provider header name,
/// secret verification and payload vocabulary so API hosts remain neutral.
/// </summary>
public sealed partial class AsaasGateway : IBankSlipProviderWebhookGateway
{
    public const string WebhookAuthenticationHeader = "asaas-access-token";

    string IBankSlipProviderWebhookGateway.ProviderCode => ProviderCodeValue;

    public async Task<bool> AuthenticateWebhookAsync(
        IReadOnlyDictionary<string, string> requestHeaders,
        BankSlipGatewayContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestHeaders);
        ArgumentNullException.ThrowIfNull(context);

        var presentedSecret = requestHeaders
            .FirstOrDefault(item => string.Equals(
                item.Key,
                WebhookAuthenticationHeader,
                StringComparison.OrdinalIgnoreCase))
            .Value;
        if (string.IsNullOrWhiteSpace(presentedSecret))
            return false;

        GatewayCredential credential;
        try
        {
            credential = await _credentialResolver
                .GetRequiredAsync(ProviderCodeValue, ToGatewayContext(context), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GatewayCredentialException)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(credential.WebhookSecret))
            return false;

        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presentedSecret));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(credential.WebhookSecret));
        return CryptographicOperations.FixedTimeEquals(presentedHash, expectedHash);
    }

    public BankSlipProviderWebhookEnvelope ParseWebhook(string requestPayload)
    {
        if (string.IsNullOrWhiteSpace(requestPayload))
            throw new FormatException("The Asaas webhook payload is empty.");

        using var document = JsonDocument.Parse(requestPayload);
        var root = document.RootElement;
        var eventId = GetString(root, "id");
        var eventName = GetString(root, "event");
        if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(eventName))
            throw new FormatException("The Asaas webhook does not contain an event id and type.");
        if (!root.TryGetProperty("payment", out var payment)
            || payment.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("The Asaas webhook does not contain a payment object.");
        }

        var externalReference = GetString(payment, "externalReference");
        var bankSlipId = TryParseBankSlipId(externalReference);
        var providerStatus = GetString(payment, "status") ?? eventName;
        var billingType = GetString(payment, "billingType");
        var value = TryGetDecimal(payment, "value");
        var eventAtUtc = TryGetDateTimeUtc(root, "dateCreated")
            ?? TryGetDateTimeUtc(payment, "lastInvoiceViewedDate")
            ?? DateTime.UtcNow;
        var paidAtUtc = TryGetDateTimeUtc(payment, "clientPaymentDate")
            ?? TryGetDateTimeUtc(payment, "paymentDate")
            ?? (MapStatus(providerStatus) == BankSlipStatus.Paid ? eventAtUtc : null);

        var providerEvent = new BankSlipProviderNotificationEvent
        {
            EventId = eventId,
            ChargeId = GetString(payment, "id"),
            CustomId = externalReference,
            EventType = string.Equals(billingType, "BOLETO", StringComparison.OrdinalIgnoreCase)
                ? "charge"
                : "payment",
            ProviderStatus = providerStatus,
            Status = MapStatus(providerStatus),
            EventAtUtc = eventAtUtc,
            PaidAtUtc = paidAtUtc,
            Value = value,
            Payload = root.GetRawText()
        };

        return new BankSlipProviderWebhookEnvelope
        {
            NotificationId = eventId,
            BankSlipId = bankSlipId,
            Batch = new BankSlipProviderNotificationBatch
            {
                ProviderCode = ProviderCodeValue,
                Events = new[] { providerEvent }
            }
        };
    }

    private static Guid TryParseBankSlipId(string? externalReference)
    {
        if (string.IsNullOrWhiteSpace(externalReference))
            return Guid.Empty;
        return Guid.TryParseExact(externalReference, "N", out var compact)
            ? compact
            : Guid.TryParse(externalReference, out var regular)
                ? regular
                : Guid.Empty;
    }

    private static decimal? TryGetDecimal(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number))
            return number;
        return property.ValueKind == JsonValueKind.String
            && decimal.TryParse(
                property.GetString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var textNumber)
                    ? textNumber
                    : null;
    }

    private static DateTime? TryGetDateTimeUtc(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value)
            || !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
        {
            return null;
        }

        return timestamp.UtcDateTime;
    }
}
