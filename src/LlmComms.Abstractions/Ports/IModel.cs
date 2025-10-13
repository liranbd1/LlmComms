namespace LlmComms.Abstractions.Ports;

/// <summary>
/// Represents metadata about a specific LLM model.
/// </summary>
public interface IModel
{
    /// <summary>
    /// Gets the model identifier (e.g., "gpt-4", "claude-3-opus").
    /// </summary>
    string ModelId { get; }

    /// <summary>
    /// Gets the maximum number of input tokens supported by this model.
    /// Returns null if unknown or unlimited.
    /// </summary>
    int? MaxInputTokens { get; }

    /// <summary>
    /// Gets the maximum number of output tokens supported by this model.
    /// Returns null if unknown or unlimited.
    /// </summary>
    int? MaxOutputTokens { get; }

    /// <summary>
    /// Gets the format type of this model (e.g., "chat", "instruct", "json").
    /// </summary>
    string Format { get; }
}