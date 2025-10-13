namespace LlmComms.Abstractions.Exceptions;

/// <summary>
/// Thrown when access is forbidden (HTTP 403).
/// </summary>
public sealed class PermissionDeniedException : LlmException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PermissionDeniedException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="requestId">The request identifier.</param>
    /// <param name="providerCode">The provider-specific error code.</param>
    /// <param name="innerException">The inner exception.</param>
    public PermissionDeniedException(
        string message,
        string? requestId = null,
        string? providerCode = null,
        Exception? innerException = null)
        : base(message, requestId, 403, providerCode ?? "forbidden", innerException)
    {
    }
}