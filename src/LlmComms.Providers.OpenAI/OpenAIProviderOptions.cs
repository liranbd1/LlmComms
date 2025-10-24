using System;
using LlmComms.Abstractions.Ports;
using ChatClient = global::OpenAI.Chat.ChatClient;
using OpenAIClientOptions = global::OpenAI.OpenAIClientOptions;

namespace LlmComms.Providers.OpenAI;

/// <summary>
/// Strongly-typed configuration applied when wiring the OpenAI provider.
/// </summary>
/// <remarks>
/// These options mirror the knobs offered by the official OpenAI .NET SDK so the provider can
/// spin up matching SDK clients under the hood. Most applications can set <see cref="ApiKey"/>
/// and optionally <see cref="DefaultModelId"/>, while advanced scenarios can override client
/// creation entirely via <see cref="ChatClientFactory"/> or plug in a custom <see cref="Transport"/>.
/// </remarks>
public sealed class OpenAIProviderOptions
{
    /// <summary>
    /// Gets or sets the API key used for authenticating with OpenAI.
    /// Provide a non-empty value unless <see cref="ChatClientFactory"/> supplies pre-authenticated
    /// SDK clients.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OpenAI API endpoint. Leave <c>null</c> to target the default api.openai.com
    /// endpoint. Set this when routing through a proxy or Azure-hosted gateway that speaks the
    /// OpenAI protocol.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the organization identifier to send with requests, when required.
    /// Only certain enterprise tenants need to forward the organization id.
    /// </summary>
    public string? Organization { get; set; }

    /// <summary>
    /// Gets or sets the default model identifier to use when one is not provided explicitly.
    /// The provider does not validate model capabilities; callers must ensure the chosen model
    /// supports their prompts and configuration.
    /// </summary>
    public string DefaultModelId { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Gets or sets a factory for creating model-specific chat clients. The factory receives
    /// the requested model identifier and must return a configured <see cref="global::OpenAI.Chat.ChatClient"/>.
    /// When omitted the provider constructs clients using <see cref="ApiKey"/>, <see cref="Endpoint"/>,
    /// and <see cref="Organization"/>.
    /// </summary>
    public Func<string, ChatClient>? ChatClientFactory { get; set; }

    /// <summary>
    /// Gets or sets a hook for configuring <see cref="global::OpenAI.OpenAIClientOptions"/> prior to client creation.
    /// This allows toggling features such as network timeouts or custom HTTP handlers.
    /// </summary>
    public Action<OpenAIClientOptions>? ConfigureClientOptions { get; set; }

    /// <summary>
    /// Gets or sets a transport override that allows callers to intercept request execution.
    /// When supplied, the provider bypasses the SDK transport entirely and delegates to the
    /// caller-provided implementation.
    /// </summary>
    public ITransport? Transport { get; set; }
}
