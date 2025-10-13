namespace LlmComms.Abstractions.Exceptions;

/// <summary>
/// Thrown when a request times out.
/// </summary>
public sealed class TimeoutException : LlmException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TimeoutException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="requestId">The request identifier.</param>
    /// <param name="innerException">The inner exception.</param>
    public TimeoutException(
        string message,
        string? requestId = null,
        Exception? innerException = null)
        : base(message, requestId, null, "timeout", innerException)
    {
    }
}