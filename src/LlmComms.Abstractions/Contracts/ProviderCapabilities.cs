namespace LlmComms.Abstractions.Contracts;

/// <summary>
/// Declares the features supported by an LLM provider.
/// </summary>
public sealed class ProviderCapabilities
{
    /// <summary>
    /// Gets or sets a value indicating whether the provider supports streaming responses.
    /// </summary>
    public bool SupportsStreaming { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the provider supports JSON mode (json_object response format).
    /// </summary>
    public bool SupportsJsonMode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the provider supports function/tool calling.
    /// </summary>
    public bool SupportsTools { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the provider supports batch requests.
    /// </summary>
    public bool SupportsBatch { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the provider supports image inputs (vision).
    /// </summary>
    public bool SupportsVision { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the provider supports audio inputs.
    /// </summary>
    public bool SupportsAudio { get; set; }
}