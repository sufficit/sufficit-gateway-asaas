namespace Sufficit.Gateway.Asaas;

/// <summary>
/// Represents a provider-level Asaas error without coupling it to a specific
/// product such as bank slips or invoices.
/// </summary>
public sealed class AsaasGatewayException : Exception
{
    public AsaasGatewayException(
        string errorCode,
        string message,
        int? httpStatusCode = null,
        Exception? innerException = null,
        TimeSpan? retryAfter = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        HttpStatusCode = httpStatusCode;
        RetryAfter = retryAfter;
    }

    public string ErrorCode { get; }
    public int? HttpStatusCode { get; }
    public TimeSpan? RetryAfter { get; }
}
