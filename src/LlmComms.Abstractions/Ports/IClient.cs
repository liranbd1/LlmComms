using LlmComms.Abstractions.Contracts;

namespace LlmComms.Abstractions.Ports;

/// <summary>
/// Defines the main entry point for sending requests to LLM providers.
/// </summary>
public interface IClient
{
    /// <summary>
    /// Sends a request to the LLM and returns the complete response.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The complete LLM response.</returns>
    Task<Response> SendAsync(Request request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a request to the LLM and streams the response as it arrives.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async stream of response events.</returns>
    IAsyncEnumerable<StreamEvent> StreamAsync(Request request, CancellationToken cancellationToken = default);
}