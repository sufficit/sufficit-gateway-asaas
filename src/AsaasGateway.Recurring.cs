using System.Net.Http;
using System.Text.Json;
using Sufficit.Gateway;

namespace Sufficit.Gateway.Asaas;

public sealed record AsaasRecurringPayment(string Id, decimal Value, string Status,
    DateOnly DueDate, string? PaymentDate, string? ClientPaymentDate);
public sealed record AsaasRecurringSnapshot(string? SubscriptionId, string Status,
    decimal? Value, DateOnly? NextDueDate, IReadOnlyList<AsaasRecurringPayment> Payments);

public sealed partial class AsaasGateway
{
    /// <summary>Reads provider evidence without returning card tokens or holder data.</summary>
    public async Task<AsaasRecurringSnapshot> GetRecurringSnapshotAsync(string checkoutId,
        string customerId, string? subscriptionId, GatewayCallContext context, CancellationToken ct)
    {
        if (!Guid.TryParse(checkoutId, out _) || !customerId.StartsWith("cus_", StringComparison.Ordinal)
            || customerId.Any(char.IsControl)) throw new ArgumentException("Invalid recurring identity.");
        var originalSubscription = subscriptionId;
        var path = "payments?limit=100&customer=" + Uri.EscapeDataString(customerId)
            + (subscriptionId is null ? "&checkoutSession=" + Uri.EscapeDataString(checkoutId)
                : "&subscription=" + Uri.EscapeDataString(subscriptionId));
        var payments = new List<AsaasRecurringPayment>();
        for (var offset = 0; ; offset += 100)
        {
            using var response = await SendGatewayAsync(() => new(HttpMethod.Get,
                BuildUri(context, path + "&offset=" + offset)), context, ct);
            response.EnsureSuccessStatusCode();
            using var document = await ReadJsonAsync(response, ct);
            var root = document.RootElement;
            foreach (var item in root.GetProperty("data").EnumerateArray())
            {
                if (GetString(item, "customer") != customerId || GetString(item, "billingType") != "CREDIT_CARD"
                    || (originalSubscription is null && GetString(item, "checkoutSession") != checkoutId))
                    throw new InvalidOperationException("Recurring customer, checkout or method mismatch.");
                var source = GetString(item, "subscription");
                if (string.IsNullOrWhiteSpace(source) || (subscriptionId is not null && subscriptionId != source))
                    throw new InvalidOperationException("Recurring subscription mismatch.");
                subscriptionId = source;
                payments.Add(new(GetString(item, "id")!, item.GetProperty("value").GetDecimal(),
                    GetString(item, "status")!, DateOnly.Parse(GetString(item, "dueDate")!),
                    GetString(item, "paymentDate"), GetString(item, "clientPaymentDate")));
            }
            if (!root.TryGetProperty("hasMore", out var more) || !more.GetBoolean()) break;
            if (root.GetProperty("data").GetArrayLength() == 0) throw new InvalidOperationException("Invalid recurring pagination.");
        }
        if (subscriptionId is null) return new(null, "PENDING", null, null, payments);
        if (!subscriptionId.StartsWith("sub_", StringComparison.Ordinal) || subscriptionId.Any(char.IsControl))
            throw new InvalidOperationException("Invalid subscription identifier.");
        using var subscriptionResponse = await SendGatewayAsync(
            () => new(HttpMethod.Get, BuildUri(context, "subscriptions/" + Uri.EscapeDataString(subscriptionId))), context, ct);
        subscriptionResponse.EnsureSuccessStatusCode();
        using var subscriptionDocument = await ReadJsonAsync(subscriptionResponse, ct);
        var s = subscriptionDocument.RootElement;
        if (GetString(s, "customer") != customerId || GetString(s, "billingType") != "CREDIT_CARD"
            || GetString(s, "cycle") != "MONTHLY") throw new InvalidOperationException("Subscription terms mismatch.");
        return new(subscriptionId, GetString(s, "status")!, s.GetProperty("value").GetDecimal(),
            DateOnly.TryParse(GetString(s, "nextDueDate"), out var next) ? next : null, payments);
    }
}
