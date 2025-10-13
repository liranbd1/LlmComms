namespace LlmComms.Abstractions.Contracts;

/// <summary>
/// Represents a collection of tools available for function calling.
/// </summary>
public record ToolCollection
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolCollection"/> record.
    /// </summary>
    /// <param name="tools">The collection of tool definitions.</param>
    public ToolCollection(IReadOnlyList<ToolDefinition> tools)
    {
        Tools = tools ?? throw new ArgumentNullException(nameof(tools));
    }

    /// <summary>
    /// Gets the collection of available tools.
    /// </summary>
    public IReadOnlyList<ToolDefinition> Tools { get; set; }
}