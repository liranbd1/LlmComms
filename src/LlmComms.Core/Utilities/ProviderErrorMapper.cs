using System;
using LlmComms.Abstractions.Exceptions;

namespace LlmComms.Core.Utilities;

/// <summary>
/// Provides shared logic for turning HTTP status codes into the exception hierarchy used by providers.
/// </summary>
public static class ProviderErrorMapper
{
    /// <summary>
    /// Creates an <see cref="Exception"/> instance appropriate for the supplied status code.
    /// </summary>
    /// <param name="statusCode">HTTP status code returned by the provider.</param>
    /// <param name="message">Human-friendly error message.</param>
    /// <param name="requestId">Correlation identifier, if available.</param>
    /// <param name="providerCode">Provider-specific error code.</param>
    /// <param name="innerException">Optional inner exception.</param>
    /// <returns>An exception derived from <see cref="LlmException"/>.</returns>
    public static Exception Map(int statusCode, string message, string? requestId = null, string? providerCode = null, Exception? innerException = null)
    {
        return statusCode switch
        {
            400 => new ValidationException(message, requestId, providerCode, innerException),
            401 => new AuthorizationException(message, requestId, providerCode, innerException),
            402 => new QuotaExceededException(message, requestId, providerCode, innerException),
            403 => new PermissionDeniedException(message, requestId, providerCode, innerException),
            404 => new ProviderUnknownException(message, requestId, innerException: innerException),
            408 => new LlmComms.Abstractions.Exceptions.TimeoutException(message, requestId, innerException),
            409 => new ProviderUnavailableException(message, requestId, statusCode: statusCode, providerCode: providerCode, innerException: innerException),
            429 => new RateLimitedException(message, retryAfter: null, requestId: requestId, providerCode: providerCode, innerException: innerException),
            500 or 502 or 503 or 504 => new ProviderUnavailableException(message, requestId, statusCode: statusCode, providerCode: providerCode, innerException: innerException),
            _ => new LlmException(message, requestId, statusCode, providerCode, innerException)
        };
    }
}
