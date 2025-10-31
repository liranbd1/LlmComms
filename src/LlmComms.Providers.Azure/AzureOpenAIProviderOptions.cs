using System;
using Azure.AI.OpenAI;
using Azure.Core;
using LlmComms.Abstractions.Ports;
using ChatClient = global::OpenAI.Chat.ChatClient;

namespace LlmComms.Providers.Azure;

/// <summary>
/// Configuration applied when wiring the Azure OpenAI provider.
/// </summary>
/// <remarks>
/// These options mirror the knobs exposed by the Azure OpenAI SDK so that callers can
/// supply credentials, override client creation, or swap in a custom transport when the default SDK
/// pipeline is not desired.
/// </remarks>
public sealed class AzureOpenAIProviderOptions
{
    /// <summary>
    /// Gets or sets the Azure resource name hosting the OpenAI deployment.
    /// This value is combined with <c>openai.azure.com</c> when constructing the default endpoint.
    /// </summary>
    public string ResourceName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional fully-qualified endpoint URI.
    /// When provided, <see cref="ResourceName"/> is ignored for REST calls and client creation.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the default deployment identifier to target when callers do not specify one explicitly.
    /// </summary>
    public string DefaultDeploymentId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the API version applied to REST requests.
    /// The default aligns with the most recent generally available Azure OpenAI release.
    /// </summary>
    public string ApiVersion { get; set; } = AzureProviderDefaults.DefaultAzureOpenAIApiVersion;

    /// <summary>
    /// Gets or sets the API key used for authenticating SDK and transport requests.
    /// Leave unset when <see cref="Credential"/> or <see cref="ChatClientFactory"/> manage authentication.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the credential used for Entra ID authentication.
    /// When supplied, the provider requests bearer tokens for Azure OpenAI audiences.
    /// </summary>
    public TokenCredential? Credential { get; set; }

    /// <summary>
    /// Gets or sets the token scope used when requesting bearer tokens for transport overrides.
    /// Defaults to the standard Azure Cognitive Services scope.
    /// </summary>
    public string TokenScope { get; set; } = AzureProviderDefaults.DefaultAzureOpenAIScope;

    /// <summary>
    /// Gets or sets a factory for creating deployment-specific chat clients. The factory receives the
    /// resolved deployment identifier and must return a configured <see cref="global::OpenAI.Chat.ChatClient"/>.
    /// </summary>
    public Func<string, ChatClient>? ChatClientFactory { get; set; }

    /// <summary>
    /// Gets or sets a hook for customizing <see cref="AzureOpenAIClientOptions"/> prior to client creation.
    /// </summary>
    public Action<AzureOpenAIClientOptions>? ConfigureClientOptions { get; set; }

    /// <summary>
    /// Gets or sets a transport override. When provided the provider bypasses the SDK and delegates
    /// request execution to the custom transport.
    /// </summary>
    public ITransport? Transport { get; set; }
}
