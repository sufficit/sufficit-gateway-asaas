using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sufficit.Gateway;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sufficit.Gateway.Asaas;

/// <summary>
/// Shared Asaas provider façade. Product capabilities are implemented in
/// separate partial files and reuse this HTTP, credential and configuration
/// boundary.
/// </summary>
public sealed partial class AsaasGateway
{
    public const string HttpClientName = "Sufficit.Gateway.Asaas";
    public const string ProviderCodeValue = "asaas";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGatewayCredentialResolver _credentialResolver;
    private readonly IOptionsMonitor<AsaasGatewayOptions> _options;
    private readonly ILogger<AsaasGateway> _logger;
    private readonly AsaasRateLimitCoordinator _rateLimits;

    public AsaasGateway(
        IHttpClientFactory httpClientFactory,
        IGatewayCredentialResolver credentialResolver,
        IOptionsMonitor<AsaasGatewayOptions> options,
        ILogger<AsaasGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _credentialResolver = credentialResolver;
        _options = options;
        _logger = logger;
        _rateLimits = new AsaasRateLimitCoordinator(logger);
    }

    private async Task<HttpResponseMessage> SendGatewayAsync(
        Func<HttpRequestMessage> requestFactory,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        var credential = await GetRequiredCredentialAsync(context, cancellationToken)
            .ConfigureAwait(false);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var options = _options.CurrentValue;
        client.Timeout = options.Timeout;
        using var request = requestFactory();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("access_token", credential.ApiKey);
        request.Headers.TryAddWithoutValidation("User-Agent", options.UserAgent);
        using var admission = _rateLimits.Admit(request.Method, context, options);

        try
        {
            var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            _rateLimits.Observe(request, response, context, options);
            return response;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AsaasGatewayException(
                "asaas_timeout",
                "The Asaas request timed out.",
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new AsaasGatewayException(
                "asaas_transport_error",
                "The Asaas API could not be reached.",
                innerException: exception);
        }
    }

    private Uri BuildUri(GatewayCallContext context, string relativePath)
    {
        var options = _options.CurrentValue;
        var baseAddress = context.Environment == GatewayEnvironment.Production
            ? options.ProductionBaseAddress
            : options.SandboxBaseAddress;
        return new Uri(baseAddress, relativePath);
    }

    private static HttpRequestMessage CreateJsonRequest(HttpMethod method, Uri uri, object payload)
        => new(method, uri)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> ReadErrorCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            if (document.RootElement.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array
                && errors.GetArrayLength() > 0)
            {
                return GetString(errors[0], "code");
            }

            return GetString(document.RootElement, "code");
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return delta;
        if (retryAfter?.Date is { } date)
        {
            var remaining = date - DateTimeOffset.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : null;
        }

        return null;
    }
}
