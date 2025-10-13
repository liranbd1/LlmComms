namespace LlmComms.Abstractions.Exceptions;

/// <summary>
/// Thrown when rate limit is exceeded (HTTP 429).
/// </summary>
public sealed class RateLimitedException : LlmException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitedException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="retryAfter">The recommended retry delay.</param>
    /// <param name="requestId">The request identifier.</param>
    /// <param name="providerCode">The provider-specific error code.</param>
    /// <param name="innerException">The inner exception.</param>
    public RateLimitedException(
        string message,
        TimeSpan? retryAfter = null,
        string? requestId = null,
        string? providerCode = null,
        Exception? innerException = null)
        : base(message, requestId, 429, providerCode ?? "rate_limited", innerException)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// Gets the recommended time to wait before retrying the request.
    /// </summary>
    public TimeSpan? RetryAfter { get; }
}