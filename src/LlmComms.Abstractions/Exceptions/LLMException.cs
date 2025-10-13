namespace LlmComms.Abstractions.Exceptions;

/// <summary>
/// Base exception for all LLM-related errors.
/// </summary>
public class LlmException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LlmException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="requestId">The request identifier for correlation.</param>
    /// <param name="statusCode">The HTTP status code, if applicable.</param>
    /// <param name="providerCode">The provider-specific error code, if available.</param>
    /// <param name="innerException">The inner exception, if any.</param>
    public LlmException(
        string message,
        string? requestId = null,
        int? statusCode = null,
        string? providerCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        RequestId = requestId;
        StatusCode = statusCode;
        ProviderCode = providerCode;
    }

    /// <summary>
    /// Gets the request identifier for correlation and tracing.
    /// </summary>
    public string? RequestId { get; }

    /// <summary>
    /// Gets the HTTP status code associated with this error, if applicable.
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// Gets the provider-specific error code, if available.
    /// </summary>
    public string? ProviderCode { get; }
}