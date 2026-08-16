using Microsoft.Extensions.DependencyInjection;
using Sufficit.Gateway;

namespace Sufficit.Gateway.Asaas.Tests;

internal static class GatewayTestFactory
{
    public static AsaasGateway CreateAsaas(
        RecordingHttpMessageHandler handler,
        Action<AsaasGatewayOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IGatewayCredentialResolver, StaticGatewayCredentialResolver>();
        services.Configure<AsaasGatewayOptions>(options =>
        {
            options.Timeout = TimeSpan.FromSeconds(5);
            options.UserAgent = "Sufficit-Gateway-Asaas.Tests/1.0";
            configure?.Invoke(options);
        });
        services.AddHttpClient(AsaasGateway.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddSingleton<AsaasGateway>();
        return services.BuildServiceProvider().GetRequiredService<AsaasGateway>();
    }
}
