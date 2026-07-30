using Microsoft.Extensions.DependencyInjection;
using Sufficit.Finance;

namespace Sufficit.Gateway.Asaas.Tests;

internal static class GatewayTestFactory
{
    public static AsaasBankSlipGateway CreateAsaas(RecordingHttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IBankSlipCredentialResolver, StaticBankSlipCredentialResolver>();
        services.Configure<AsaasBankSlipGatewayOptions>(options =>
        {
            options.Timeout = TimeSpan.FromSeconds(5);
            options.UserAgent = "Sufficit-BankSlips.Tests/1.0";
        });
        services.AddHttpClient(AsaasBankSlipGateway.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddSingleton<AsaasBankSlipGateway>();
        return services.BuildServiceProvider().GetRequiredService<AsaasBankSlipGateway>();
    }
}
