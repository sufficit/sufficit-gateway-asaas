using Sufficit.Gateway;
using System.Globalization;

namespace Sufficit.Gateway.Asaas;

public sealed partial class AsaasGateway : IAsaasFinancialStatementGateway
{
    public async Task<AsaasFinancialTransactionPage> ListFinancialTransactionsAsync(
        AsaasFinancialTransactionSearchParameters parameters,
        GatewayCallContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(context);
        if (parameters.Offset < 0)
            throw new ArgumentOutOfRangeException(nameof(parameters), "Offset cannot be negative.");
        if (parameters.Limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(parameters), "Limit must be between 1 and 100.");
        if (parameters.StartDate == default || parameters.FinishDate < parameters.StartDate)
            throw new ArgumentException("A valid statement period is required.", nameof(parameters));
        var order = string.Equals(parameters.Order, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";
        var query = string.Join("&",
            $"offset={parameters.Offset.ToString(CultureInfo.InvariantCulture)}",
            $"limit={parameters.Limit.ToString(CultureInfo.InvariantCulture)}",
            $"startDate={parameters.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
            $"finishDate={parameters.FinishDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
            $"order={order}");

        using var response = await SendGatewayAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BuildUri(context, "financialTransactions?" + query)),
            context,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "statement", cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<AsaasFinancialTransactionPage>(response, cancellationToken).ConfigureAwait(false);
    }
}
