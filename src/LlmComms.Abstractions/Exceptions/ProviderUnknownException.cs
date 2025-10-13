namespace LlmComms.Abstractions.Exceptions;

/// <summary>
/// Thrown when an unknown or unclassified provider error occurs.
/// </summary>
public sealed class ProviderUnknownException : LlmException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderUnknownException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="requestId">The request identifier.</param>
    /// <param name="statusCode">The HTTP status code, if available.</param>
    /// <param name="providerCode">The provider-specific error code.</param>
    /// <param name="innerException">The inner exception.</param>
    public ProviderUnknownException(
        string message,
        string? requestId = null,
        int? statusCode = null,
        string? providerCode = null,
        Exception? innerException = null)
        : base(message, requestId, statusCode, providerCode ?? "unknown", innerException)
    {
    }
}