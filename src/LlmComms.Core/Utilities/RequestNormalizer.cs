using LlmComms.Abstractions.Contracts;

namespace LlmComms.Core.Utilities;

/// <summary>
/// Normalizes requests for deterministic hashing and caching.
/// </summary>
public static class RequestNormalizer
{
    /// <summary>
    /// Normalizes a request by removing volatile fields and ensuring deterministic ordering.
    /// </summary>
    /// <param name="request">The request to normalize.</param>
    /// <returns>A normalized copy of the request suitable for hashing.</returns>
    public static Request Normalize(Request request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        // Create a new request without volatile fields
        return new Request(request.Messages)
        {
            Tools = request.Tools,
            Temperature = request.Temperature,
            TopP = request.TopP,
            MaxOutputTokens = request.MaxOutputTokens,
            ResponseFormat = request.ResponseFormat,
            // Explicitly exclude ProviderHints - they're volatile
            ProviderHints = null
        };
    }
}