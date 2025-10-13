namespace LlmComms.Abstractions.Exceptions;

/// <summary>
/// Thrown when API quota or credit limit is exceeded (HTTP 402 or quota headers).
/// </summary>
public sealed class QuotaExceededException : LlmException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QuotaExceededException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="requestId">The request identifier.</param>
    /// <param name="providerCode">The provider-specific error code.</param>
    /// <param name="innerException">The inner exception.</param>
    public QuotaExceededException(
        string message,
        string? requestId = null,
        string? providerCode = null,
        Exception? innerException = null)
        : base(message, requestId, 402, providerCode ?? "quota_exceeded", innerException)
    {
    }
}