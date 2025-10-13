namespace LlmComms.Abstractions.Contracts;

/// <summary>
/// Defines a tool or function that can be called by the LLM.
/// </summary>
public record ToolDefinition
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolDefinition"/> record.
    /// </summary>
    /// <param name="name">The name of the tool.</param>
    /// <param name="description">A description of what the tool does.</param>
    /// <param name="jsonSchema">JSON Schema defining the tool's parameters.</param>
    public ToolDefinition(string name, string description, IReadOnlyDictionary<string, object> jsonSchema)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        JsonSchema = jsonSchema ?? throw new ArgumentNullException(nameof(jsonSchema));
    }

    /// <summary>
    /// Gets the name of the tool.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets a description of what the tool does.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets the JSON Schema (draft-07 subset) defining the tool's parameters.
    /// </summary>
    public IReadOnlyDictionary<string, object> JsonSchema { get; set; }
}