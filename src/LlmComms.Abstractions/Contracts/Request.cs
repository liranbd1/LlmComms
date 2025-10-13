namespace LlmComms.Abstractions.Contracts;

/// <summary>
/// Specifies the desired format for the LLM response.
/// </summary>
public enum ResponseFormat
{
    /// <summary>
    /// Plain text response (default).
    /// </summary>
    Text,

    /// <summary>
    /// Response must be valid JSON object.
    /// </summary>
    JsonObject
}

public record Request
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Request"/> record.
    /// </summary>
    /// <param name="messages">The conversation history.</param>
    public Request(IReadOnlyList<Message> messages)
    {
        Messages = messages ?? throw new ArgumentNullException(nameof(messages));
    }

    /// <summary>
    /// Gets the conversation history.
    /// </summary>
    public IReadOnlyList<Message> Messages { get; set; }

    /// <summary>
    /// Gets the available tools for function calling.
    /// </summary>
    public ToolCollection? Tools { get; set; }

    /// <summary>
    /// Gets the sampling temperature (0.0 to 2.0). Higher values make output more random.
    /// </summary>
    public double? Temperature { get; set; }

    /// <summary>
    /// Gets the nucleus sampling threshold. Alternative to temperature.
    /// </summary>
    public double? TopP { get; set; }

    /// <summary>
    /// Gets the maximum number of tokens to generate in the completion.
    /// </summary>
    public int? MaxOutputTokens { get; set; }

    /// <summary>
    /// Gets the desired response format (text or json_object).
    /// </summary>
    public ResponseFormat? ResponseFormat { get; set; }

    /// <summary>
    /// Gets provider-specific configuration hints for this request.
    /// </summary>
    public IReadOnlyDictionary<string, object>? ProviderHints { get; set; }
}
