namespace LlmComms.Abstractions.Exceptions;

/// <summary>
/// Thrown when the provider service is unavailable (HTTP 5xx).
/// </summary>
public sealed class ProviderUnavailableException : LlmException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderUnavailableException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="requestId">The request identifier.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="providerCode">The provider-specific error code.</param>
    /// <param name="innerException">The inner exception.</param>
    public ProviderUnavailableException(
        string message,
        string? requestId = null,
        int statusCode = 503,
        string? providerCode = null,
        Exception? innerException = null)
        : base(message, requestId, statusCode, providerCode ?? "provider_unavailable", innerException)
    {
    }
}