using Sufficit.Gateway;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sufficit.Gateway.Asaas;

public sealed partial class AsaasGateway : IAsaasWebhookGateway
{
    private const string SequentialSendType = "SEQUENTIALLY";
    private static readonly Regex EventCodePattern = new(
        "^[A-Z][A-Z0-9_]{2,99}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<IReadOnlyList<AsaasWebhookSubscription>> ListAsync(
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        using var response = await SendGatewayAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BuildUri(context, "webhooks?offset=0&limit=100")),
            context,
            cancellationToken).ConfigureAwait(false);
        await EnsureWebhookSuccessAsync(response, "asaas_webhooks_list_failed", cancellationToken)
            .ConfigureAwait(false);
        using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        return ParseWebhookList(document.RootElement);
    }

    public async Task<AsaasWebhookProvisioningResult> EnsureAsync(
        AsaasWebhookProvisioningRequest request,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ValidateProvisioningRequest(request);
        ArgumentNullException.ThrowIfNull(context);
        var credential = await GetRequiredCredentialAsync(context, cancellationToken).ConfigureAwait(false);
        ValidateWebhookSecret(credential.WebhookSecret);

        var existing = (await ListAsync(context, cancellationToken).ConfigureAwait(false))
            .Where(item => string.Equals(item.Name, request.Name, StringComparison.Ordinal))
            .ToArray();
        if (existing.Length > 1)
        {
            throw new AsaasGatewayException(
                "asaas_webhook_duplicate_name",
                "More than one Asaas webhook uses the requested integration name.");
        }

        if (existing.Length == 0)
        {
            var created = await WriteWebhookAsync(
                HttpMethod.Post,
                "webhooks",
                request,
                credential.WebhookSecret!,
                includeApiVersion: true,
                cancellationToken,
                context).ConfigureAwait(false);
            return new AsaasWebhookProvisioningResult
            {
                Outcome = AsaasWebhookProvisioningOutcome.Created,
                Subscription = created
            };
        }

        var current = existing[0];
        if (!request.ForceUpdate && ConfigurationMatches(current, request))
        {
            return new AsaasWebhookProvisioningResult
            {
                Outcome = AsaasWebhookProvisioningOutcome.Unchanged,
                Subscription = current
            };
        }

        var updated = await WriteWebhookAsync(
            HttpMethod.Put,
            $"webhooks/{Uri.EscapeDataString(current.Id)}",
            request,
            credential.WebhookSecret!,
            includeApiVersion: false,
            cancellationToken,
            context).ConfigureAwait(false);
        return new AsaasWebhookProvisioningResult
        {
            Outcome = AsaasWebhookProvisioningOutcome.Updated,
            Subscription = updated
        };
    }

    private async Task<AsaasWebhookSubscription> WriteWebhookAsync(
        HttpMethod method,
        string relativePath,
        AsaasWebhookProvisioningRequest request,
        string authenticationToken,
        bool includeApiVersion,
        CancellationToken cancellationToken,
        GatewayCallContext context)
    {
        var payload = new Dictionary<string, object?>
        {
            ["name"] = request.Name,
            ["url"] = request.Url.AbsoluteUri,
            ["email"] = request.NotificationEmail,
            ["enabled"] = request.Enabled,
            ["interrupted"] = false,
            ["authToken"] = authenticationToken,
            ["sendType"] = SequentialSendType,
            ["events"] = NormalizeEvents(request.Events)
        };
        if (includeApiVersion)
            payload["apiVersion"] = 3;

        using var response = await SendGatewayAsync(
            () => CreateJsonRequest(method, BuildUri(context, relativePath), payload),
            context,
            cancellationToken).ConfigureAwait(false);
        await EnsureWebhookSuccessAsync(response, "asaas_webhook_write_failed", cancellationToken)
            .ConfigureAwait(false);
        using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        return ParseWebhook(document.RootElement);
    }

    private async Task EnsureWebhookSuccessAsync(
        HttpResponseMessage response,
        string fallbackCode,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var providerCode = await ReadErrorCodeAsync(response, cancellationToken).ConfigureAwait(false);
        throw new AsaasGatewayException(
            providerCode ?? fallbackCode,
            response.StatusCode == HttpStatusCode.Unauthorized
                ? "Asaas rejected the configured API credential."
                : "Asaas rejected the webhook configuration request.",
            (int)response.StatusCode,
            retryAfter: ReadRetryAfter(response));
    }

    private async Task<GatewayCredential> GetRequiredCredentialAsync(
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        GatewayCredential credential;
        try
        {
            credential = await _credentialResolver
                .GetRequiredAsync(ProviderCodeValue, context, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GatewayCredentialException exception)
        {
            throw new AsaasGatewayException(
                "asaas_credentials_missing",
                "Asaas credentials are not configured for the selected tenant.",
                innerException: exception);
        }

        if (string.IsNullOrWhiteSpace(credential.ApiKey))
        {
            throw new AsaasGatewayException(
                "asaas_credentials_missing",
                "Asaas credentials are not configured for the selected tenant.");
        }

        return credential;
    }

    private static IReadOnlyList<AsaasWebhookSubscription> ParseWebhookList(JsonElement root)
    {
        var values = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
                ? data
                : default;
        if (values.ValueKind != JsonValueKind.Array)
            return Array.Empty<AsaasWebhookSubscription>();
        return values.EnumerateArray().Select(ParseWebhook).ToArray();
    }

    private static AsaasWebhookSubscription ParseWebhook(JsonElement value)
    {
        var rawUrl = GetString(value, "url");
        return new AsaasWebhookSubscription
        {
            Id = GetString(value, "id") ?? string.Empty,
            Name = GetString(value, "name") ?? string.Empty,
            Url = Uri.TryCreate(rawUrl, UriKind.Absolute, out var url) ? url : null,
            NotificationEmail = GetString(value, "email"),
            Enabled = GetWebhookBoolean(value, "enabled"),
            Interrupted = GetWebhookBoolean(value, "interrupted"),
            SendType = GetString(value, "sendType") ?? string.Empty,
            Events = value.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array
                ? events.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()!)
                    .ToArray()
                : Array.Empty<string>()
        };
    }

    private static bool ConfigurationMatches(
        AsaasWebhookSubscription current,
        AsaasWebhookProvisioningRequest requested)
        => current.Url == requested.Url
            && string.Equals(current.NotificationEmail, requested.NotificationEmail, StringComparison.OrdinalIgnoreCase)
            && current.Enabled == requested.Enabled
            && !current.Interrupted
            && string.Equals(current.SendType, SequentialSendType, StringComparison.OrdinalIgnoreCase)
            && current.Events.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(NormalizeEvents(requested.Events));

    private static string[] NormalizeEvents(IEnumerable<string> events)
        => events.Select(value => value.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static void ValidateProvisioningRequest(AsaasWebhookProvisioningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 120)
            throw new ArgumentException("A webhook name with at most 120 characters is required.", nameof(request));
        if (request.Url == null
            || !request.Url.IsAbsoluteUri
            || !string.Equals(request.Url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(request.Url.UserInfo)
            || !string.IsNullOrEmpty(request.Url.Fragment))
        {
            throw new ArgumentException("A public HTTPS webhook URL is required.", nameof(request));
        }
        try
        {
            _ = new MailAddress(request.NotificationEmail);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("A notification email is required.", nameof(request), exception);
        }

        var events = NormalizeEvents(request.Events);
        if (events.Length == 0 || events.Length > 111 || events.Any(value => !EventCodePattern.IsMatch(value)))
            throw new ArgumentException("At least one valid Asaas event code is required.", nameof(request));
    }

    private static void ValidateWebhookSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length is < 32 or > 255
            || value.Any(char.IsWhiteSpace))
        {
            throw new AsaasGatewayException(
                "asaas_webhook_secret_invalid",
                "The protected Asaas webhook secret must contain between 32 and 255 non-whitespace characters.");
        }
    }

    private static bool GetWebhookBoolean(JsonElement value, string propertyName)
        => value.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            && property.GetBoolean();
}
