namespace LlmComms.Abstractions.Contracts;

/// <summary>
/// Specifies why an LLM completion finished.
/// </summary>
public enum FinishReason
{
    /// <summary>
    /// The model reached a natural stopping point or a provided stop sequence.
    /// </summary>
    Stop,

    /// <summary>
    /// The maximum token limit was reached.
    /// </summary>
    Length,

    /// <summary>
    /// The model called a tool/function.
    /// </summary>
    ToolCall
}


/// <summary>
/// Represents a response from an LLM provider.
/// </summary>
public record Response
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Response"/> record.
    /// </summary>
    /// <param name="output">The assistant's response message.</param>
    /// <param name="usage">Token usage information for this request.</param>
    public Response(Message output, Usage usage)
    {
        Output = output ?? throw new ArgumentNullException(nameof(output));
        Usage = usage ?? throw new ArgumentNullException(nameof(usage));
    }

    /// <summary>
    /// Gets the assistant's response message.
    /// </summary>
    public Message Output { get; set; }

    /// <summary>
    /// Gets token usage information for this request.
    /// </summary>
    public Usage Usage { get; set; }

    /// <summary>
    /// Gets the reason why the completion finished (stop, length, tool_call, or null).
    /// </summary>
    public FinishReason? FinishReason { get; set; }

    /// <summary>
    /// Gets the list of tool calls requested by the model, if any.
    /// </summary>
    public IReadOnlyList<ToolCall>? ToolCalls { get; set; }

    /// <summary>
    /// Gets raw provider-specific data that doesn't fit the standard schema.
    /// </summary>
    public IReadOnlyDictionary<string, object>? ProviderRaw { get; set; }
}
