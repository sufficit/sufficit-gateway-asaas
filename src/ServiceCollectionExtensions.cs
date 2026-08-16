using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sufficit.Finance;

namespace Sufficit.Gateway.Asaas;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSufficitGatewayAsaas(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AsaasGatewayOptions>()
            .Bind(configuration.GetSection(AsaasGatewayOptions.SectionName));
        services.AddHttpClient(AsaasGateway.HttpClientName);

        services.TryAddSingleton<AsaasGateway>();
        services.AddSingleton<IBankSlipGateway>(
            serviceProvider => serviceProvider.GetRequiredService<AsaasGateway>());
        services.AddSingleton<IBankSlipProviderDiagnosticsGateway>(
            serviceProvider => serviceProvider.GetRequiredService<AsaasGateway>());
        services.AddSingleton<IBankSlipProviderWebhookGateway>(
            serviceProvider => serviceProvider.GetRequiredService<AsaasGateway>());
        services.AddSingleton<IGatewayDiagnosticsGateway>(
            serviceProvider => serviceProvider.GetRequiredService<AsaasGateway>());
        services.TryAddSingleton<IAsaasInvoiceGateway>(
            serviceProvider => serviceProvider.GetRequiredService<AsaasGateway>());
        services.TryAddSingleton<IAsaasWebhookGateway>(
            serviceProvider => serviceProvider.GetRequiredService<AsaasGateway>());
        services.TryAddSingleton<IAsaasRateLimitMonitor>(
            serviceProvider => serviceProvider.GetRequiredService<AsaasGateway>());

        return services;
    }
}
