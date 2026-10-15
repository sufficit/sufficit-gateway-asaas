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
    /// <summary>Normalizes the future price while preserving the initial charge, even before it is paid.</summary>
    public async Task<AsaasRecurringSnapshot> ReconcileRecurringAmountAsync(string checkoutId, string customerId,
        string? subscriptionId, decimal initialAmount, DateOnly initialDueDate, decimal monthlyAmount,
        DateOnly monthlyFirstDueDate, GatewayCallContext context, CancellationToken ct)
    {
        if (initialAmount < 5 || monthlyAmount < 5 || decimal.Round(monthlyAmount, 2) != monthlyAmount
            || monthlyFirstDueDate <= initialDueDate)
            throw new ArgumentException("Invalid recurring price schedule.");
        // Always anchor the subscription to the original provider checkout, including retries.
        var anchor = await GetRecurringSnapshotAsync(checkoutId, customerId, null, context, ct);
        if (subscriptionId is not null && anchor.SubscriptionId != subscriptionId)
            throw new InvalidOperationException("Recurring checkout binding mismatch.");
        if (anchor.SubscriptionId is null) return anchor;
        var snapshot = await GetRecurringSnapshotAsync(checkoutId, customerId, anchor.SubscriptionId, context, ct);
        ValidateRecurringSchedule(snapshot, initialAmount, initialDueDate, monthlyAmount, monthlyFirstDueDate);
        if (snapshot.Value == monthlyAmount && snapshot.Payments.All(p => p.DueDate < monthlyFirstDueDate
            || p.Value == monthlyAmount || p.Status is "DELETED" or "REFUNDED" or "CANCELLED")) return snapshot;
        if (!snapshot.Payments.Any(p => p.DueDate == initialDueDate && p.Value == initialAmount))
            throw new InvalidOperationException("Initial recurring charge is missing.");
        // Never apply the monthly price to all pending charges: the first may still be unpaid.
        if (snapshot.Value != monthlyAmount)
        {
            using var response = await SendGatewayAsync(() => CreateJsonRequest(HttpMethod.Put,
                BuildUri(context, "subscriptions/" + Uri.EscapeDataString(snapshot.SubscriptionId!)),
                new { value = monthlyAmount, updatePendingPayments = false }), context, ct);
            response.EnsureSuccessStatusCode();
        }
        // Asaas may have pre-generated the next installment; correct only those future pending charges.
        foreach (var payment in snapshot.Payments.Where(p => p.DueDate >= monthlyFirstDueDate && p.Value != monthlyAmount
            && p.Status is "PENDING" or "OVERDUE" or "AWAITING_RISK_ANALYSIS"))
        {
            if (!payment.Id.StartsWith("pay_", StringComparison.Ordinal) || payment.Id.Any(char.IsControl))
                throw new InvalidOperationException("Invalid recurring payment identity.");
            using var response = await SendGatewayAsync(() => CreateJsonRequest(HttpMethod.Put,
                BuildUri(context, "payments/" + Uri.EscapeDataString(payment.Id)), new { value = monthlyAmount, billingType = "CREDIT_CARD", dueDate = payment.DueDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) }), context, ct);
            response.EnsureSuccessStatusCode();
        }
        var updated = await GetRecurringSnapshotAsync(checkoutId, customerId, snapshot.SubscriptionId, context, ct);
        ValidateRecurringSchedule(updated, initialAmount, initialDueDate, monthlyAmount, monthlyFirstDueDate);
        if (updated.Value != monthlyAmount || updated.Payments.Any(p => p.DueDate >= monthlyFirstDueDate
            && p.Status is not ("DELETED" or "REFUNDED" or "CANCELLED") && p.Value != monthlyAmount))
            throw new InvalidOperationException("Recurring price update requires reconciliation.");
        return updated;
    }

    private static void ValidateRecurringSchedule(AsaasRecurringSnapshot snapshot, decimal initialAmount,
        DateOnly initialDueDate, decimal monthlyAmount, DateOnly monthlyFirstDueDate)
    {
        if (snapshot.Value != initialAmount && snapshot.Value != monthlyAmount)
            throw new InvalidOperationException("Unexpected recurring price.");
        foreach (var p in snapshot.Payments.Where(p => p.Status is not ("DELETED" or "REFUNDED" or "CANCELLED")))
        {
            if (p.DueDate == initialDueDate && p.Value == initialAmount) continue;
            if (p.DueDate >= monthlyFirstDueDate && (p.Value == monthlyAmount
                || p.Value == initialAmount
                    && p.Status is "PENDING" or "OVERDUE" or "AWAITING_RISK_ANALYSIS")) continue;
            throw new InvalidOperationException("Unexpected recurring receipt schedule.");
        }
    }

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
            || GetString(s, "cycle") != "MONTHLY" || !string.IsNullOrWhiteSpace(GetString(s, "endDate"))
            || s.TryGetProperty("maxPayments", out var max) && max.ValueKind == JsonValueKind.Number && max.GetInt32() > 0) throw new InvalidOperationException("Subscription terms mismatch.");
        return new(subscriptionId, GetString(s, "status")!, s.GetProperty("value").GetDecimal(),
            DateOnly.TryParse(GetString(s, "nextDueDate"), out var next) ? next : null, payments);
    }
}
