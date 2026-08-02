using Sufficit.Gateway;

namespace Sufficit.Gateway.Asaas.Tests;

internal sealed class StaticGatewayCredentialResolver : IGatewayCredentialResolver
{
    public Task<GatewayCredential> GetRequiredAsync(
        string providerCode,
        GatewayCallContext context,
        CancellationToken cancellationToken)
        => Task.FromResult(new GatewayCredential
        {
            ApiKey = "$aact_hmlg_test"
        });
}
