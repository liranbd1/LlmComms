using System;
using LlmComms.Abstractions.Ports;

namespace LlmComms.Providers.Azure;

/// <summary>
/// Metadata wrapper describing a specific Azure OpenAI deployment.
/// </summary>
public sealed class AzureOpenAIModel : IModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AzureOpenAIModel"/> class.
    /// </summary>
    /// <param name="deploymentId">The Azure OpenAI deployment identifier.</param>
    /// <param name="format">Optional format description (defaults to <c>chat</c>).</param>
    public AzureOpenAIModel(string deploymentId, string format = "chat")
    {
        if (string.IsNullOrWhiteSpace(deploymentId))
            throw new ArgumentException("Deployment identifier must be provided.", nameof(deploymentId));

        DeploymentId = deploymentId;
        Format = format;
    }

    /// <inheritdoc />
    public string ModelId => DeploymentId;

    /// <summary>
    /// Gets the deployment identifier backing the model.
    /// </summary>
    public string DeploymentId { get; }

    /// <inheritdoc />
    public int? MaxInputTokens => null;

    /// <inheritdoc />
    public int? MaxOutputTokens => null;

    /// <inheritdoc />
    public string Format { get; }
}
