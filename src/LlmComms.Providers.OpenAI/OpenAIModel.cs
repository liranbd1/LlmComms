using System;
using LlmComms.Abstractions.Ports;

namespace LlmComms.Providers.OpenAI;

/// <summary>
/// Minimal metadata wrapper describing the OpenAI model requested by the caller.
/// </summary>
public sealed class OpenAIModel : IModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAIModel"/> class.
    /// </summary>
    /// <param name="modelId">The OpenAI model identifier.</param>
    /// <param name="format">The format category of the model (defaults to <c>chat</c>).</param>
    public OpenAIModel(string modelId, string format = "chat")
    {
        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("Model identifier must be provided.", nameof(modelId));

        ModelId = modelId;
        Format = format;
    }

    /// <inheritdoc />
    public string ModelId { get; }

    /// <inheritdoc />
    public int? MaxInputTokens => null;

    /// <inheritdoc />
    public int? MaxOutputTokens => null;

    /// <inheritdoc />
    public string Format { get; }
}
