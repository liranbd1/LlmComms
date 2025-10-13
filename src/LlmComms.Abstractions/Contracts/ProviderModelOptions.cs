namespace LlmComms.Abstractions.Contracts;

/// <summary>
/// Provides model-specific configuration overrides.
/// </summary>
public sealed class ProviderModelOptions
{
    /// <summary>
    /// Gets or sets the default maximum output tokens for this specific model.
    /// When set, overrides <see cref="ClientOptions.DefaultMaxOutputTokens"/>.
    /// </summary>
    public int? DefaultMaxOutputTokens { get; set; }
}