using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sufficit.Finance;

namespace Sufficit.Gateway.Asaas;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSufficitAsaasBankSlipGateway(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AsaasBankSlipGatewayOptions>()
            .Bind(configuration.GetSection(AsaasBankSlipGatewayOptions.SectionName));
        services.AddHttpClient(AsaasBankSlipGateway.HttpClientName);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IBankSlipGateway, AsaasBankSlipGateway>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IBankSlipProviderDiagnosticsGateway, AsaasBankSlipGateway>());

        return services;
    }
}
