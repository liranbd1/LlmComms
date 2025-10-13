using LlmComms.Abstractions.Ports;
namespace LlmComms.Abstractions.Contracts;

/// <summary>
/// Provides the complete execution context for an LLM request, passed through the middleware pipeline.
/// </summary>
public sealed class LLMContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LLMContext"/> class.
    /// </summary>
    /// <param name="provider">The LLM provider being used.</param>
    /// <param name="model">The specific model being invoked.</param>
    /// <param name="request">The LLM request.</param>
    /// <param name="callContext">The per-request correlation context.</param>
    /// <param name="options">The client configuration options.</param>
    /// <param name="cancellationToken">The cancellation token for this request.</param>
    public LLMContext(
        IProvider provider,
        IModel model,
        Request request,
        ProviderCallContext callContext,
        ClientOptions options,
        CancellationToken cancellationToken)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Request = request ?? throw new ArgumentNullException(nameof(request));
        CallContext = callContext ?? throw new ArgumentNullException(nameof(callContext));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        CancellationToken = cancellationToken;
    }

    /// <summary>
    /// Gets the LLM provider being used for this request.
    /// </summary>
    public IProvider Provider { get; }

    /// <summary>
    /// Gets the specific model being invoked.
    /// </summary>
    public IModel Model { get; }

    /// <summary>
    /// Gets the LLM request being processed.
    /// </summary>
    public Request Request { get; }

    /// <summary>
    /// Gets the per-request correlation and telemetry context.
    /// </summary>
    public ProviderCallContext CallContext { get; }

    /// <summary>
    /// Gets the client configuration options.
    /// </summary>
    public ClientOptions Options { get; }

    /// <summary>
    /// Gets the cancellation token for this request.
    /// </summary>
    public CancellationToken CancellationToken { get; }
}