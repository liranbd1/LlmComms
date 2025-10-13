namespace LlmComms.Abstractions.Contracts;

/// <summary>
/// Provides client-level configuration options.
/// </summary>
public sealed class ClientOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether to throw an exception when JSON mode produces invalid JSON.
    /// Default is true (strict mode).
    /// </summary>
    public bool ThrowOnInvalidJson { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to emit token usage events for metrics.
    /// Default is true.
    /// </summary>
    public bool EnableTokenUsageEvents { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to redact sensitive data in logs and traces.
    /// Default is true.
    /// </summary>
    public bool EnableRedaction { get; set; } = true;

    /// <summary>
    /// Gets or sets the default maximum number of tokens to generate if not specified in the request.
    /// Default is 512.
    /// </summary>
    public int DefaultMaxOutputTokens { get; set; } = 512;

    /// <summary>
    /// Gets or sets a value indicating whether to combine final text chunks in streaming responses.
    /// Default is false.
    /// </summary>
    public bool CoalesceFinalStreamText { get; set; } = false;
}