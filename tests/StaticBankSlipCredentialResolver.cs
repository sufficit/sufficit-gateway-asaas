using Sufficit.Finance;

namespace Sufficit.Gateway.Asaas.Tests;

internal sealed class StaticBankSlipCredentialResolver : IBankSlipCredentialResolver
{
    public Task<BankSlipProviderCredential> GetRequiredAsync(
        string providerCode,
        BankSlipGatewayContext context,
        CancellationToken cancellationToken)
        => Task.FromResult(new BankSlipProviderCredential
        {
            ApiKey = "$aact_hmlg_test"
        });
}
