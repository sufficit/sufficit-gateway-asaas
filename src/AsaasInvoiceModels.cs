using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sufficit.Gateway.Asaas;

public sealed class AsaasInvoice
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("customer")]
    public string? CustomerId { get; set; }

    [JsonPropertyName("payment")]
    public string? PaymentId { get; set; }

    [JsonPropertyName("installment")]
    public string? InstallmentId { get; set; }

    [JsonPropertyName("externalReference")]
    public string? ExternalReference { get; set; }

    [JsonPropertyName("value")]
    public decimal? Value { get; set; }

    [JsonPropertyName("effectiveDate")]
    public DateOnly? EffectiveDate { get; set; }

    [JsonPropertyName("number")]
    public string? Number { get; set; }

    [JsonPropertyName("validationCode")]
    public string? ValidationCode { get; set; }

    [JsonPropertyName("pdfUrl")]
    public Uri? PdfUrl { get; set; }

    [JsonPropertyName("xmlUrl")]
    public Uri? XmlUrl { get; set; }

    [JsonPropertyName("serviceDescription")]
    public string? ServiceDescription { get; set; }

    [JsonPropertyName("observations")]
    public string? Observations { get; set; }

    [JsonPropertyName("taxes")]
    public JsonElement? Taxes { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class AsaasCustomer
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("cpfCnpj")]
    public string? Document { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class AsaasInvoiceDocument
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = "application/octet-stream";
}

public sealed class AsaasInvoicePage
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
    public IList<AsaasInvoice> Data { get; set; } = new List<AsaasInvoice>();

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class AsaasInvoiceSearchParameters
{
    public int Offset { get; set; }
    public int Limit { get; set; } = 20;
    public DateOnly? EffectiveDateFrom { get; set; }
    public DateOnly? EffectiveDateTo { get; set; }
    public string? PaymentId { get; set; }
    public string? InstallmentId { get; set; }
    public string? CustomerId { get; set; }
    public string? ExternalReference { get; set; }
    public string? Status { get; set; }
}

public sealed class AsaasInvoiceScheduleRequest
{
    [JsonPropertyName("payment")]
    public string? PaymentId { get; set; }

    [JsonPropertyName("installment")]
    public string? InstallmentId { get; set; }

    [JsonPropertyName("customer")]
    public string? CustomerId { get; set; }

    [JsonPropertyName("serviceDescription")]
    public string ServiceDescription { get; set; } = string.Empty;

    [JsonPropertyName("observations")]
    public string Observations { get; set; } = string.Empty;

    [JsonPropertyName("externalReference")]
    public string? ExternalReference { get; set; }

    [JsonPropertyName("value")]
    public decimal Value { get; set; }

    [JsonPropertyName("deductions")]
    public decimal Deductions { get; set; }

    [JsonPropertyName("effectiveDate")]
    public DateOnly EffectiveDate { get; set; }

    [JsonPropertyName("municipalServiceId")]
    public string? MunicipalServiceId { get; set; }

    [JsonPropertyName("municipalServiceCode")]
    public string? MunicipalServiceCode { get; set; }

    [JsonPropertyName("municipalServiceName")]
    public string? MunicipalServiceName { get; set; }

    [JsonPropertyName("updatePayment")]
    public bool? UpdatePayment { get; set; }

    [JsonPropertyName("taxes")]
    public JsonElement Taxes { get; set; }

    [JsonPropertyName("useTaxSystemReformNT007")]
    public bool? UseTaxSystemReformNT007 { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class AsaasInvoiceUpdateRequest
{
    [JsonPropertyName("serviceDescription")]
    public string? ServiceDescription { get; set; }

    [JsonPropertyName("observations")]
    public string? Observations { get; set; }

    [JsonPropertyName("externalReference")]
    public string? ExternalReference { get; set; }

    [JsonPropertyName("value")]
    public decimal? Value { get; set; }

    [JsonPropertyName("deductions")]
    public decimal? Deductions { get; set; }

    [JsonPropertyName("effectiveDate")]
    public DateOnly? EffectiveDate { get; set; }

    [JsonPropertyName("updatePayment")]
    public bool? UpdatePayment { get; set; }

    [JsonPropertyName("taxes")]
    public JsonElement? Taxes { get; set; }

    [JsonPropertyName("useTaxSystemReformNT007")]
    public bool? UseTaxSystemReformNT007 { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class AsaasInvoiceCancelRequest
{
    [JsonPropertyName("cancelOnlyOnAsaas")]
    public bool? CancelOnlyOnAsaas { get; set; }
}
