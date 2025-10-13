using LlmComms.Abstractions.Contracts;

namespace LlmComms.Abstractions.Ports;

/// <summary>
/// Defines the interface that all LLM provider adapters must implement.
/// </summary>
public interface IProvider
{
    /// <summary>
    /// Gets the name of the provider (e.g., "openai", "anthropic", "azure").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the capabilities supported by this provider.
    /// </summary>
    ProviderCapabilities Capabilities { get; }

    /// <summary>
    /// Creates a model instance with the specified identifier.
    /// </summary>
    /// <param name="modelId">The model identifier (e.g., "gpt-4", "claude-3-opus").</param>
    /// <param name="options">Optional model-specific configuration.</param>
    /// <returns>A model instance.</returns>
    IModel CreateModel(string modelId, ProviderModelOptions? options = null);

    /// <summary>
    /// Sends a non-streaming request to the LLM.
    /// </summary>
    /// <param name="model">The model to use.</param>
    /// <param name="request">The request to send.</param>
    /// <param name="context">The call context for correlation and telemetry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The complete LLM response.</returns>
    Task<Response> SendAsync(
        IModel model,
        Request request,
        ProviderCallContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends a streaming request to the LLM.
    /// </summary>
    /// <param name="model">The model to use.</param>
    /// <param name="request">The request to send.</param>
    /// <param name="context">The call context for correlation and telemetry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async stream of response events.</returns>
    IAsyncEnumerable<StreamEvent> StreamAsync(
        IModel model,
        Request request,
        ProviderCallContext context,
        CancellationToken cancellationToken);
}