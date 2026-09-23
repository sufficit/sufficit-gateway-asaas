using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sufficit.Gateway.Asaas;

/// <summary>
/// Billing types accepted and returned by the Asaas payments API, in the
/// exact upper-case form Asaas uses.
/// </summary>
public static class AsaasPaymentBillingTypes
{
    public const string Boleto = "BOLETO";
    public const string Pix = "PIX";
    public const string CreditCard = "CREDIT_CARD";
    public const string Unidentified = "UNIDENTIFIED";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        Boleto,
        Pix,
        CreditCard,
        Unidentified
    };

    public static bool IsKnown(string? value)
        => value is not null && Known.Contains(value);
}

public sealed class AsaasPayment
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("customer")]
    public string? CustomerId { get; set; }

    [JsonPropertyName("value")]
    public decimal? Value { get; set; }

    [JsonPropertyName("netValue")]
    public decimal? NetValue { get; set; }

    [JsonPropertyName("billingType")]
    public string? BillingType { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("dueDate")]
    public DateOnly? DueDate { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("externalReference")]
    public string? ExternalReference { get; set; }

    [JsonPropertyName("invoiceUrl")]
    public Uri? InvoiceUrl { get; set; }

    [JsonPropertyName("bankSlipUrl")]
    public Uri? BankSlipUrl { get; set; }

    [JsonPropertyName("invoiceNumber")]
    public string? InvoiceNumber { get; set; }

    [JsonPropertyName("subscription")]
    public string? SubscriptionId { get; set; }

    [JsonPropertyName("installment")]
    public string? InstallmentId { get; set; }

    /// <summary>
    /// Asaas formats timestamps as "yyyy-MM-dd HH:mm:ss"; kept as the raw
    /// provider string to avoid assuming a timezone.
    /// </summary>
    [JsonPropertyName("dateCreated")]
    public string? DateCreated { get; set; }

    [JsonPropertyName("clientPaymentDate")]
    public string? ClientPaymentDate { get; set; }

    [JsonPropertyName("paymentDate")]
    public string? PaymentDate { get; set; }

    [JsonPropertyName("confirmedDate")]
    public string? ConfirmedDate { get; set; }

    [JsonPropertyName("estimatedCreditDate")]
    public string? EstimatedCreditDate { get; set; }

    [JsonPropertyName("transactionReceiptUrl")]
    public Uri? TransactionReceiptUrl { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class AsaasPaymentCreateRequest
{
    [JsonPropertyName("customer")]
    public string CustomerId { get; set; } = string.Empty;

    [JsonPropertyName("billingType")]
    public string BillingType { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public decimal Value { get; set; }

    [JsonPropertyName("dueDate")]
    public DateOnly DueDate { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("externalReference")]
    public string? ExternalReference { get; set; }

    [JsonPropertyName("installmentCount")]
    public int? InstallmentCount { get; set; }

    [JsonPropertyName("installmentValue")]
    public decimal? InstallmentValue { get; set; }

    [JsonPropertyName("postalService")]
    public bool? PostalService { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class AsaasPaymentPage
{
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("hasMore")]
    public bool HasMore { get; set; }

    [JsonPropertyName("totalCount")]
    public int? TotalCount { get; set; }

    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    [JsonPropertyName("offset")]
    public int? Offset { get; set; }

    [JsonPropertyName("data")]
    public IList<AsaasPayment> Data { get; set; } = new List<AsaasPayment>();

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class AsaasPaymentSearchParameters
{
    public int Offset { get; set; }
    public int Limit { get; set; } = 20;
    public string? CustomerId { get; set; }
    public string? ExternalReference { get; set; }
    public string? Status { get; set; }
    public string? BillingType { get; set; }
}

public sealed class AsaasPaymentStatus
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class AsaasIdentificationField
{
    [JsonPropertyName("identificationField")]
    public string? IdentificationField { get; set; }

    [JsonPropertyName("barCode")]
    public string? BarCode { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class AsaasPixQrCode
{
    [JsonPropertyName("payload")]
    public string? Payload { get; set; }

    [JsonPropertyName("encodedImage")]
    public string? EncodedImage { get; set; }

    /// <summary>
    /// Asaas formats timestamps as "yyyy-MM-dd HH:mm:ss"; kept as the raw
    /// provider string to avoid assuming a timezone.
    /// </summary>
    [JsonPropertyName("expirationDate")]
    public string? ExpirationDate { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class AsaasDeletionResult
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
