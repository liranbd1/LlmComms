namespace LlmComms.Abstractions.Exceptions;

/// <summary>
/// Thrown when authentication fails (HTTP 401).
/// </summary>
public sealed class AuthorizationException : LlmException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="requestId">The request identifier.</param>
    /// <param name="providerCode">The provider-specific error code.</param>
    /// <param name="innerException">The inner exception.</param>
    public AuthorizationException(
        string message,
        string? requestId = null,
        string? providerCode = null,
        Exception? innerException = null)
        : base(message, requestId, 401, providerCode ?? "unauthorized", innerException)
    {
    }
}