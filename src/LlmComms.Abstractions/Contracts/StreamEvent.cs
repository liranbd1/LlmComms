namespace LlmComms.Abstractions.Contracts;

/// <summary>
/// Represents an event in a streaming LLM response.
/// </summary>
public record StreamEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StreamEvent"/> record.
    /// </summary>
    /// <param name="kind">The type of event.</param>
    public StreamEvent(StreamEventKind kind)
    {
        Kind = kind;
    }

    /// <summary>
    /// Gets the type of streaming event.
    /// </summary>
    public StreamEventKind Kind { get; set; }

    /// <summary>
    /// Gets the incremental text chunk (for delta events).
    /// </summary>
    public string? TextDelta { get; set; }

    /// <summary>
    /// Gets the incremental tool call data (for tool_call events).
    /// </summary>
    public ToolCall? ToolCallDelta { get; set; }

    /// <summary>
    /// Gets the incremental token usage (for complete events).
    /// </summary>
    public Usage? UsageDelta { get; set; }

    /// <summary>
    /// Gets a value indicating whether this is the terminal event (exactly once on graceful completion).
    /// </summary>
    public bool IsTerminal { get; set; }
}

/// <summary>
/// Specifies the type of streaming event.
/// </summary>
public enum StreamEventKind
{
    /// <summary>
    /// Incremental text content.
    /// </summary>
    Delta,

    /// <summary>
    /// Tool/function call data.
    /// </summary>
    ToolCall,

    /// <summary>
    /// Completion with final usage data.
    /// </summary>
    Complete,

    /// <summary>
    /// Error occurred during streaming.
    /// </summary>
    Error
}