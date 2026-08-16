using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sufficit.Finance;
using Sufficit.Gateway;
using Xunit;

namespace Sufficit.Gateway.Asaas.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void RegistrationExposesOneProviderFacadeThroughEveryCapability()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IGatewayCredentialResolver, StaticGatewayCredentialResolver>();
        services.AddSufficitGatewayAsaas(configuration);

        using var provider = services.BuildServiceProvider();
        var gateway = provider.GetRequiredService<AsaasGateway>();

        Assert.Same(gateway, provider.GetRequiredService<IBankSlipGateway>());
        Assert.Same(gateway, provider.GetRequiredService<IBankSlipProviderDiagnosticsGateway>());
        Assert.Same(gateway, provider.GetRequiredService<IBankSlipProviderWebhookGateway>());
        Assert.Same(gateway, provider.GetRequiredService<IAsaasWebhookGateway>());
        Assert.Same(gateway, provider.GetRequiredService<IGatewayDiagnosticsGateway>());
        Assert.Same(gateway, provider.GetRequiredService<IAsaasInvoiceGateway>());
        Assert.Same(gateway, provider.GetRequiredService<IAsaasRateLimitMonitor>());
    }
}
