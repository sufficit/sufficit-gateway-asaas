namespace Sufficit.Gateway.Asaas;

/// <summary>
/// Configures the Asaas API client.
/// </summary>
public sealed class AsaasBankSlipGatewayOptions
{
    public const string SectionName = "BankSlips:Providers:Asaas";
    public Uri SandboxBaseAddress { get; set; } = new("https://api-sandbox.asaas.com/v3/");
    public Uri ProductionBaseAddress { get; set; } = new("https://api.asaas.com/v3/");
    public string UserAgent { get; set; } = "Sufficit-BankSlips/2.0 (.NET)";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
