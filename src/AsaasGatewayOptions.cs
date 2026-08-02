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
}
