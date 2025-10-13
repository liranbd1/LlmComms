namespace LlmComms.Abstractions.Contracts;

/// <summary>
/// Represents a tool invocation requested by the LLM.
/// </summary>
public record ToolCall
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolCall"/> record.
    /// </summary>
    /// <param name="name">The name of the tool being invoked.</param>
    /// <param name="argumentsJson">The tool arguments as a JSON string.</param>
    public ToolCall(string name, string argumentsJson)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ArgumentsJson = argumentsJson ?? throw new ArgumentNullException(nameof(argumentsJson));
    }

    /// <summary>
    /// Gets the name of the tool being invoked.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets the tool arguments as a raw JSON string.
    /// </summary>
    public string ArgumentsJson { get; set; }
}