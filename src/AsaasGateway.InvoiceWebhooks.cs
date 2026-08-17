using Sufficit.Gateway;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sufficit.Gateway.Asaas;

public sealed partial class AsaasGateway : IAsaasInvoiceWebhookGateway
{
    public async Task<bool> AuthenticateAsync(
        IReadOnlyDictionary<string, string> requestHeaders,
        GatewayCallContext context,
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
                .GetRequiredAsync(ProviderCodeValue, context, cancellationToken)
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

    public AsaasInvoiceWebhookEnvelope Parse(string requestPayload)
    {
        if (string.IsNullOrWhiteSpace(requestPayload))
            throw new FormatException("The Asaas invoice webhook payload is empty.");

        using var document = JsonDocument.Parse(requestPayload);
        var root = document.RootElement;
        var eventId = GetString(root, "id")?.Trim();
        var eventType = GetString(root, "event")?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(eventType))
            throw new FormatException("The Asaas invoice webhook does not contain an event id and type.");
        if (eventId.Length > 200 || eventType.Length > 100)
            throw new FormatException("The Asaas invoice webhook identity is too long.");
        if (!root.TryGetProperty("invoice", out var invoiceElement)
            || invoiceElement.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("The Asaas invoice webhook does not contain an invoice object.");
        }

        var invoice = invoiceElement.Deserialize<AsaasInvoice>(JsonOptions)
            ?? throw new FormatException("The Asaas invoice webhook contains an invalid invoice object.");
        if (string.IsNullOrWhiteSpace(invoice.Id) || invoice.Id.Length > 64)
            throw new FormatException("The Asaas invoice webhook does not contain a valid invoice id.");

        var eventAt = GetString(root, "dateCreated");
        var eventAtUtc = DateTimeOffset.TryParse(
            eventAt,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
                ? parsed
                : DateTimeOffset.UtcNow;

        return new AsaasInvoiceWebhookEnvelope
        {
            EventId = eventId,
            EventType = eventType,
            EventAtUtc = eventAtUtc,
            Invoice = invoice,
            RawPayload = root.GetRawText()
        };
    }
}
