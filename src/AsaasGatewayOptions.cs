namespace Sufficit.Gateway.Asaas;

/// <summary>
/// Configures the Asaas API client shared by all provider capabilities.
/// </summary>
public sealed class AsaasGatewayOptions
{
    public const string SectionName = "Sufficit:Gateway:Asaas";
    public Uri SandboxBaseAddress { get; set; } = new("https://api-sandbox.asaas.com/v3/");
    public Uri ProductionBaseAddress { get; set; } = new("https://api.asaas.com/v3/");
    public string UserAgent { get; set; } = "Sufficit-Gateway-Asaas/2.0 (.NET)";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum GET requests in flight per credential in this process. Asaas
    /// currently accepts at most 50; the lower default leaves capacity for
    /// other consumers of the same account.
    /// </summary>
    public int MaxConcurrentGetRequests { get; set; } = 40;

    /// <summary>
    /// Provider-wide quota documented by Asaas for each account window.
    /// </summary>
    public int QuotaLimit { get; set; } = 25_000;

    /// <summary>
    /// Capacity intentionally not consumed by this gateway process.
    /// </summary>
    public int QuotaReserve { get; set; } = 5_000;

    /// <summary>
    /// Enables the conservative process-local quota guard. This guard cannot
    /// account for requests made by other applications or instances.
    /// </summary>
    public bool EnforceLocalQuotaLimit { get; set; } = true;

    public TimeSpan QuotaWindow { get; set; } = TimeSpan.FromHours(12);
    public TimeSpan DefaultRateLimitBackoff { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan ConcurrentLimitBackoff { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan MaximumProviderBackoff { get; set; } = TimeSpan.FromHours(12);
    public int LowRateLimitRemainingThreshold { get; set; } = 10;

    /// <summary>
    /// Hosts accepted for document URLs delivered in invoice webhooks. The
    /// gateway refuses arbitrary URLs to avoid turning the worker into an SSRF
    /// proxy. Subdomains of an allowed host are accepted.
    /// </summary>
    public string[] InvoiceDocumentHosts { get; set; } =
    [
        "api.notagateway.com.br",
        "www.asaas.com"
    ];

    public long MaxInvoiceDocumentBytes { get; set; } = 20 * 1024 * 1024;
}
