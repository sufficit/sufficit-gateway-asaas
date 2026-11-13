using Sufficit.Gateway;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sufficit.Gateway.Asaas;

/// <summary>Read-only access to the Asaas account statement (/v3/financialTransactions).</summary>
public interface IAsaasFinancialStatementGateway
{
    Task<AsaasFinancialTransactionPage> ListFinancialTransactionsAsync(
        AsaasFinancialTransactionSearchParameters parameters,
        GatewayCallContext context,
        CancellationToken cancellationToken);
}

public sealed class AsaasFinancialTransactionSearchParameters
{
    public DateOnly StartDate { get; set; }
    public DateOnly FinishDate { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; } = 100;
    /// <summary>"asc" or "desc".</summary>
    public string Order { get; set; } = "asc";
}

public sealed class AsaasFinancialTransactionPage
{
    [JsonPropertyName("hasMore")]
    public bool HasMore { get; set; }

    [JsonPropertyName("totalCount")]
    public int? TotalCount { get; set; }

    [JsonPropertyName("data")]
    public IList<AsaasFinancialTransaction> Data { get; set; } = new List<AsaasFinancialTransaction>();
}

/// <summary>One statement line. Negative values are charges (e.g. INVOICE_FEE).</summary>
public sealed class AsaasFinancialTransaction
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public decimal Value { get; set; }

    [JsonPropertyName("date")]
    public DateOnly? Date { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("paymentId")]
    public string? PaymentId { get; set; }

    [JsonPropertyName("invoiceId")]
    public string? InvoiceId { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
