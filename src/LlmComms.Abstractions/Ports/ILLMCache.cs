using System.Threading;
using System.Threading.Tasks;
using LlmComms.Abstractions.Contracts;

namespace LlmComms.Abstractions.Ports;

/// <summary>
/// Defines the contract for caching LLM responses.
/// </summary>
public interface ILLMCache
{
    /// <summary>
    /// Attempts to retrieve a cached response for the specified key.
    /// </summary>
    /// <param name="key">The cache key (deterministic for a request).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached response, or null if not present or expired.</returns>
    Task<Response?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a response in the cache for the specified key.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="response">The response to cache.</param>
    /// <param name="ttl">Time-to-live for the cache entry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetAsync(string key, Response response, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a cached response for the specified key.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveAsync(string key, CancellationToken cancellationToken);
}
