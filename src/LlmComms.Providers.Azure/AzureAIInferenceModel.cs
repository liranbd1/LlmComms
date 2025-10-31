using System;
using LlmComms.Abstractions.Ports;

namespace LlmComms.Providers.Azure;

/// <summary>
/// Metadata wrapper for Azure AI Inference models or deployments.
/// </summary>
public sealed class AzureAIInferenceModel : IModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AzureAIInferenceModel"/> class.
    /// </summary>
    /// <param name="modelId">The Azure AI Inference model or deployment identifier.</param>
    /// <param name="format">Optional format descriptor (defaults to <c>chat</c>).</param>
    public AzureAIInferenceModel(string modelId, string format = "chat")
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
