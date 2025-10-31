using System;
using Azure.AI.Inference;
using Azure.Core;
using LlmComms.Abstractions.Ports;

namespace LlmComms.Providers.Azure;

/// <summary>
/// Configuration surface for the Azure AI Inference provider.
/// </summary>
/// <remarks>
/// These options allow callers to align with the Azure AI Foundry client SDK or bypass it entirely
/// through a custom transport implementation.
/// </remarks>
public sealed class AzureAIInferenceProviderOptions
{
    /// <summary>
    /// Gets or sets the Azure AI Inference endpoint.
    /// The URI should point to the Foundry deployment, for example <c>https://myhub.eastus2.inference.ai.azure.com</c>.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the default model or deployment identifier to target.
    /// </summary>
    public string DefaultModelId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the API version applied to REST requests.
    /// </summary>
    public string ApiVersion { get; set; } = AzureProviderDefaults.DefaultAzureAIInferenceApiVersion;

    /// <summary>
    /// Gets or sets the API key used for authenticating SDK and transport requests.
    /// Leave unset when <see cref="Credential"/> or <see cref="ClientFactory"/> provide authentication.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the credential used for Entra ID authentication.
    /// </summary>
    public TokenCredential? Credential { get; set; }

    /// <summary>
    /// Gets or sets a factory for creating model-specific <see cref="ChatCompletionsClient"/> instances.
    /// The factory receives the resolved model identifier and must return a configured client.
    /// </summary>
    public Func<string, ChatCompletionsClient>? ClientFactory { get; set; }

    /// <summary>
    /// Gets or sets a hook for customizing <see cref="ChatCompletionsClientOptions"/> prior to client creation.
    /// </summary>
    public Action<ChatCompletionsClientOptions>? ConfigureClientOptions { get; set; }

    /// <summary>
    /// Gets or sets a transport override allowing callers to intercept request execution.
    /// </summary>
    public ITransport? Transport { get; set; }
}
