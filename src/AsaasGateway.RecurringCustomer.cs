using System.Net.Http;
using Sufficit.Gateway;

namespace Sufficit.Gateway.Asaas;

public sealed partial class AsaasGateway
{
    /// <summary>Resolve the hosted payer from provider checkout evidence, not an editable CPF.</summary>
    public async Task<string?> GetRecurringCustomerAsync(string checkoutId, GatewayCallContext context, CancellationToken ct)
    {
        if (!Guid.TryParse(checkoutId, out _)) throw new ArgumentException("Invalid checkout.");
        string? customer = null;
        for (var offset = 0; ; offset += 100)
        {
            using var response = await SendGatewayAsync(() => new(HttpMethod.Get,
                BuildUri(context, "payments?limit=100&checkoutSession=" + Uri.EscapeDataString(checkoutId) + "&offset=" + offset)), context, ct);
            response.EnsureSuccessStatusCode();
            using var document = await ReadJsonAsync(response, ct);
            var root = document.RootElement;
            foreach (var payment in root.GetProperty("data").EnumerateArray())
            {
                if (GetString(payment, "checkoutSession") != checkoutId || GetString(payment, "billingType") != "CREDIT_CARD"
                    || GetString(payment, "customer") is not { } id || !id.StartsWith("cus_", StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(GetString(payment, "subscription")) || customer is not null && customer != id)
                    throw new InvalidOperationException("Hosted payer binding mismatch.");
                customer = id;
            }
            if (!root.TryGetProperty("hasMore", out var more) || !more.GetBoolean()) break;
            if (root.GetProperty("data").GetArrayLength() == 0) throw new InvalidOperationException("Invalid pagination.");
        }
        return customer;
    }
}
