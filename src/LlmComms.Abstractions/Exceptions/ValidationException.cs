namespace LlmComms.Abstractions.Exceptions;

/// <summary>
/// Thrown when request validation fails (HTTP 400/422).
/// </summary>
public sealed class ValidationException : LlmException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="requestId">The request identifier.</param>
    /// <param name="providerCode">The provider-specific error code.</param>
    /// <param name="innerException">The inner exception.</param>
    public ValidationException(
        string message,
        string? requestId = null,
        string? providerCode = null,
        Exception? innerException = null)
        : base(message, requestId, 400, providerCode ?? "validation_error", innerException)
    {
    }
}