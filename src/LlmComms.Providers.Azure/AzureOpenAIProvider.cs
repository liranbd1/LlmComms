using System;
using System.ClientModel;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Azure.Core;
using LlmComms.Abstractions.Contracts;
using LlmComms.Abstractions.Exceptions;
using LlmComms.Abstractions.Ports;
using LlmComms.Core.Transport;
using LlmComms.Core.Utilities;
using LlmComms.Core.Utilities.Azure;
using OpenAI.Chat;
using ChatClient = global::OpenAI.Chat.ChatClient;

namespace LlmComms.Providers.Azure;

/// <summary>
/// Provider adapter for Azure OpenAI deployments.
/// </summary>
/// <remarks>
/// The provider mirrors the behavior of the OpenAI adapter while targeting Azure-hosted deployments.
/// When a custom <see cref="ITransport"/> is registered the provider bypasses the SDK and emits
/// REST payloads shaped by <see cref="AzureProviderRequestBuilder"/>.
/// </remarks>
public sealed class AzureOpenAIProvider : IProvider
{
    private readonly AzureOpenAIProviderOptions _options;
    private readonly ITransport? _transportOverride;
    private readonly ConcurrentDictionary<string, ChatClient> _chatClients = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions TransportSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureOpenAIProvider"/> class with default options.
    /// </summary>
    public AzureOpenAIProvider()
        : this(new AzureOpenAIProviderOptions(), transport: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureOpenAIProvider"/> class.
    /// </summary>
    /// <param name="options">Provider configuration aligning with the Azure OpenAI SDK.</param>
    public AzureOpenAIProvider(AzureOpenAIProviderOptions options)
        : this(options, transport: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureOpenAIProvider"/> class with a custom transport.
    /// </summary>
    /// <param name="options">Provider configuration aligning with the Azure OpenAI SDK.</param>
    /// <param name="transport">Custom transport implementation used instead of the SDK.</param>
    public AzureOpenAIProvider(AzureOpenAIProviderOptions options, ITransport? transport)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _transportOverride = transport ?? options.Transport;
    }

    /// <inheritdoc />
    public string Name => "azure-openai";

    /// <inheritdoc />
    public IModel CreateModel(string modelId, ProviderModelOptions? options = null)
    {
        _ = options; // Reserved for future Azure OpenAI model customization.

        if (string.IsNullOrWhiteSpace(modelId))
        {
            if (!string.IsNullOrWhiteSpace(_options.DefaultDeploymentId))
            {
                modelId = _options.DefaultDeploymentId;
            }
            else
            {
                throw new ArgumentException("Model identifier must be provided when no default deployment is configured.", nameof(modelId));
            }
        }

        return new AzureOpenAIModel(modelId);
    }

    /// <inheritdoc />
    public Task<Response> SendAsync(
        IModel model,
        Request request,
        ProviderCallContext context,
        CancellationToken cancellationToken)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        if (_transportOverride != null)
        {
            return SendWithTransportAsync(model, request, context, cancellationToken);
        }

        return SendWithSdkAsync(model, request, context, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<StreamEvent> StreamAsync(
        IModel model,
        Request request,
        ProviderCallContext context,
        CancellationToken cancellationToken)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        if (_transportOverride != null)
        {
            return StreamWithTransportAsync(model, request, context, cancellationToken);
        }

        return StreamWithSdkAsync(model, request, context, cancellationToken);
    }

    private async Task<Response> SendWithSdkAsync(
        IModel model,
        Request request,
        ProviderCallContext context,
        CancellationToken cancellationToken)
    {
        var deploymentId = ResolveDeploymentId(model);
        var chatClient = GetChatClient(deploymentId);
        var messages = BuildChatMessages(request);
        var options = BuildCompletionOptions(request);

        try
        {
            var completion = await chatClient.CompleteChatAsync(messages, options, cancellationToken).ConfigureAwait(false);
            return MapChatCompletion(completion);
        }
        catch (ClientResultException ex)
        {
            throw MapClientResultException(ex, context.RequestId);
        }
    }

    private Task<Response> SendWithTransportAsync(
        IModel model,
        Request request,
        ProviderCallContext context,
        CancellationToken cancellationToken)
    {
        return SendWithTransportCoreAsync(model, request, context, cancellationToken);
    }

    private async IAsyncEnumerable<StreamEvent> StreamWithSdkAsync(
        IModel model,
        Request request,
        ProviderCallContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var deploymentId = ResolveDeploymentId(model);
        var chatClient = GetChatClient(deploymentId);
        var messages = BuildChatMessages(request);
        var options = BuildCompletionOptions(request);

        AsyncCollectionResult<StreamingChatCompletionUpdate> updates;
        Usage? lastUsage = null;

        try
        {
            updates = chatClient.CompleteChatStreamingAsync(messages, options, cancellationToken);
        }
        catch (ClientResultException ex)
        {
            throw MapClientResultException(ex, context.RequestId);
        }

        await foreach (var update in updates.WithCancellation(cancellationToken))
        {
            if (update.ContentUpdate != null && update.ContentUpdate.Count > 0)
            {
                var delta = ExtractText(update.ContentUpdate);
                if (!string.IsNullOrEmpty(delta))
                {
                    yield return new StreamEvent(StreamEventKind.Delta)
                    {
                        TextDelta = delta
                    };
                }
            }

            if (update.Usage != null)
            {
                lastUsage = new Usage(update.Usage.InputTokenCount, update.Usage.OutputTokenCount, update.Usage.TotalTokenCount);
            }
        }

        yield return new StreamEvent(StreamEventKind.Complete)
        {
            UsageDelta = lastUsage,
            IsTerminal = true
        };
    }

    private async IAsyncEnumerable<StreamEvent> StreamWithTransportAsync(
        IModel model,
        Request request,
        ProviderCallContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var deploymentId = ResolveDeploymentId(model);
        var endpoint = BuildChatEndpoint(deploymentId);
        var payload = BuildTransportPayload(request, stream: true);
        var body = JsonSerializer.Serialize(payload, TransportSerializerOptions);

        var additionalHeaders = BuildAdditionalHeadersInternal(context, streaming: true);
        var bearerToken = await AcquireBearerTokenAsync(cancellationToken).ConfigureAwait(false);
        var headers = AzureProviderRequestBuilder.BuildHeaders(
            apiKey: _options.ApiKey,
            bearerToken: bearerToken,
            additionalHeaders: additionalHeaders);

        var transportRequest = new HttpTransportRequest
        {
            Url = endpoint.ToString(),
            Method = "POST",
            Headers = headers,
            Body = body
        };

        var transportResponse = await _transportOverride!.SendAsync(transportRequest, cancellationToken).ConfigureAwait(false);
        var (statusCode, responseBody) = TransportResponseReader.Read(transportResponse);

        if (statusCode >= 400)
        {
            throw MapTransportError(statusCode, responseBody, context.RequestId);
        }

        foreach (var streamEvent in ParseStreamingResponse(responseBody))
        {
            yield return streamEvent;
        }
    }

    private ChatClient GetChatClient(string deploymentId)
    {
        if (string.IsNullOrWhiteSpace(deploymentId))
            throw new ArgumentException("Deployment identifier must be provided.", nameof(deploymentId));

        if (_options.ChatClientFactory != null)
        {
            return _options.ChatClientFactory.Invoke(deploymentId);
        }

        return _chatClients.GetOrAdd(deploymentId, CreateChatClient);
    }

    private ChatClient CreateChatClient(string deploymentId)
    {
        var endpoint = ResolveEndpoint();
        var clientOptions = CreateClientOptions();

        AzureOpenAIClient azureClient;

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            azureClient = new AzureOpenAIClient(endpoint, new ApiKeyCredential(_options.ApiKey), clientOptions);
        }
        else if (_options.Credential != null)
        {
            azureClient = new AzureOpenAIClient(endpoint, _options.Credential, clientOptions);
        }
        else
        {
            throw new InvalidOperationException("Azure OpenAI provider requires an API key, TokenCredential, or chat client factory.");
        }

        return azureClient.GetChatClient(deploymentId);
    }

    private Uri ResolveEndpoint()
    {
        if (_options.Endpoint != null)
            return _options.Endpoint;

        if (!string.IsNullOrWhiteSpace(_options.ResourceName))
        {
            return new Uri($"https://{_options.ResourceName}.openai.azure.com");
        }

        throw new InvalidOperationException("Azure OpenAI provider requires either an Endpoint or ResourceName.");
    }

    private string ResolveDeploymentId(IModel model)
    {
        var deploymentId = model switch
        {
            AzureOpenAIModel azureModel => azureModel.DeploymentId,
            _ => model.ModelId
        };

        if (!string.IsNullOrWhiteSpace(deploymentId))
        {
            return deploymentId;
        }

        if (!string.IsNullOrWhiteSpace(_options.DefaultDeploymentId))
        {
            return _options.DefaultDeploymentId;
        }

        throw new InvalidOperationException("Azure OpenAI provider requires a deployment identifier on the model or options.");
    }

    private static IReadOnlyList<ChatMessage> BuildChatMessages(Request request)
    {
        var messages = new List<ChatMessage>(request.Messages.Count);

        foreach (var message in request.Messages)
        {
            switch (message.Role)
            {
                case MessageRole.System:
                    messages.Add(new SystemChatMessage(message.Content));
                    break;
                case MessageRole.User:
                    messages.Add(new UserChatMessage(message.Content));
                    break;
                case MessageRole.Assistant:
                    messages.Add(new AssistantChatMessage(message.Content));
                    break;
                case MessageRole.Function:
                    messages.Add(new ToolChatMessage(Guid.NewGuid().ToString("N"), message.Content));
                    break;
                default:
                    messages.Add(new UserChatMessage(message.Content));
                    break;
            }
        }

        return messages;
    }

    private static ChatCompletionOptions BuildCompletionOptions(Request request)
    {
        var options = new ChatCompletionOptions();

        if (request.Temperature.HasValue)
        {
            options.Temperature = (float)request.Temperature.Value;
        }

        if (request.TopP.HasValue)
        {
            options.TopP = (float)request.TopP.Value;
        }

        if (request.MaxOutputTokens.HasValue)
        {
            options.MaxOutputTokenCount = request.MaxOutputTokens.Value;
        }

        if (request.ResponseFormat is ResponseFormat.JsonObject)
        {
            options.ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat();
        }

        if (request.Tools?.Tools.Count > 0)
        {
            foreach (var tool in request.Tools.Tools)
            {
                options.Tools.Add(ConvertTool(tool));
            }
        }

        return options;
    }

    private static ChatTool ConvertTool(ToolDefinition tool)
    {
        BinaryData? schemaPayload = null;

        if (tool.JsonSchema.Count > 0)
        {
            var serialized = System.Text.Json.JsonSerializer.Serialize(tool.JsonSchema);
            schemaPayload = BinaryData.FromString(serialized);
        }

        return ChatTool.CreateFunctionTool(
            functionName: tool.Name,
            functionDescription: tool.Description,
            functionParameters: schemaPayload);
    }

    private Response MapChatCompletion(ChatCompletion completion)
    {
        var text = ExtractText(completion.Content);
        var message = new Message(MessageRole.Assistant, text);

        var usage = completion.Usage != null
            ? new Usage(completion.Usage.InputTokenCount, completion.Usage.OutputTokenCount, completion.Usage.TotalTokenCount)
            : new Usage(0, 0, 0);

        var response = new Response(message, usage)
        {
            FinishReason = MapFinishReason(completion.FinishReason),
            ToolCalls = MapToolCalls(completion.ToolCalls),
            ProviderRaw = BuildProviderRaw(completion)
        };

        return response;
    }

    private static string ExtractText(ChatMessageContent content)
    {
        if (content == null || content.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();

        foreach (var part in content)
        {
            if (part.Kind == ChatMessageContentPartKind.Text && part.Text != null)
            {
                builder.Append(part.Text);
            }
        }

        return builder.ToString();
    }

    private static string ExtractText(IReadOnlyList<ChatMessageContentPart> parts)
    {
        if (parts == null || parts.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();

        foreach (var part in parts)
        {
            if (part.Kind == ChatMessageContentPartKind.Text && part.Text != null)
            {
                builder.Append(part.Text);
            }
        }

        return builder.ToString();
    }

    private static FinishReason? MapFinishReason(ChatFinishReason finishReason)
    {
        return finishReason switch
        {
            ChatFinishReason.Stop => FinishReason.Stop,
            ChatFinishReason.Length => FinishReason.Length,
            ChatFinishReason.ToolCalls => FinishReason.ToolCall,
            _ => null
        };
    }

    private static IReadOnlyList<ToolCall>? MapToolCalls(IReadOnlyList<ChatToolCall>? toolCalls)
    {
        if (toolCalls == null || toolCalls.Count == 0)
            return null;

        var result = new List<ToolCall>(toolCalls.Count);

        foreach (var toolCall in toolCalls)
        {
            if (toolCall.Kind != ChatToolCallKind.Function || toolCall.FunctionName == null)
                continue;

            var argumentsJson = toolCall.FunctionArguments.ToString() ?? string.Empty;
            result.Add(new ToolCall(toolCall.FunctionName, argumentsJson));
        }

        return result.Count == 0 ? null : result;
    }

    private static IReadOnlyDictionary<string, object> BuildProviderRaw(ChatCompletion completion)
    {
        var raw = new Dictionary<string, object>
        {
            ["id"] = completion.Id,
            ["model"] = completion.Model,
            ["created"] = completion.CreatedAt.ToUnixTimeSeconds()
        };

        if (!string.IsNullOrEmpty(completion.SystemFingerprint))
        {
            raw["systemFingerprint"] = completion.SystemFingerprint!;
        }

        return raw;
    }

    private Exception MapClientResultException(ClientResultException exception, string? requestId)
    {
        var status = exception.Status;
        var message = exception.Message;

        if (status == 0)
        {
            return new LlmException(message, requestId, null, null, exception);
        }

        return ProviderErrorMapper.Map(status, message, requestId, null, exception);
    }

    private AzureOpenAIClientOptions CreateClientOptions()
    {
        var clientOptions = new AzureOpenAIClientOptions();
        _options.ConfigureClientOptions?.Invoke(clientOptions);
        return clientOptions;
    }

    private async Task<Response> SendWithTransportCoreAsync(
        IModel model,
        Request request,
        ProviderCallContext context,
        CancellationToken cancellationToken)
    {
        var deploymentId = ResolveDeploymentId(model);
        var endpoint = BuildChatEndpoint(deploymentId);
        var payload = BuildTransportPayload(request, stream: false);
        var body = JsonSerializer.Serialize(payload, TransportSerializerOptions);

        var additionalHeaders = BuildAdditionalHeaders(context);
        var bearerToken = await AcquireBearerTokenAsync(cancellationToken).ConfigureAwait(false);
        var headers = AzureProviderRequestBuilder.BuildHeaders(
            apiKey: _options.ApiKey,
            bearerToken: bearerToken,
            additionalHeaders: additionalHeaders);

        var transportRequest = new HttpTransportRequest
        {
            Url = endpoint.ToString(),
            Method = "POST",
            Headers = headers,
            Body = body
        };

        var transportResponse = await _transportOverride!.SendAsync(transportRequest, cancellationToken).ConfigureAwait(false);
        var (statusCode, responseBody) = TransportResponseReader.Read(transportResponse);

        if (statusCode >= 400)
        {
            throw MapTransportError(statusCode, responseBody, context.RequestId);
        }

        return MapTransportResponse(responseBody);
    }

    private Uri BuildChatEndpoint(string deploymentId)
    {
        var path = $"/openai/deployments/{deploymentId}/chat/completions";

        if (_options.Endpoint != null)
        {
            return AzureProviderRequestBuilder.BuildInferenceEndpoint(_options.Endpoint, path, _options.ApiVersion);
        }

        if (!string.IsNullOrWhiteSpace(_options.ResourceName))
        {
            return AzureProviderRequestBuilder.BuildOpenAIEndpoint(_options.ResourceName, path, _options.ApiVersion);
        }

        throw new InvalidOperationException("Azure OpenAI provider requires either an Endpoint or ResourceName for transport execution.");
    }

    private static IDictionary<string, object?> BuildTransportPayload(Request request, bool stream)
    {
        var payload = new Dictionary<string, object?>
        {
            ["messages"] = AzureProviderRequestBuilder.BuildMessages(request.Messages)
        };

        if (stream)
        {
            payload["stream"] = true;
        }

        if (request.Temperature.HasValue)
        {
            payload["temperature"] = request.Temperature.Value;
        }

        if (request.TopP.HasValue)
        {
            payload["top_p"] = request.TopP.Value;
        }

        if (request.MaxOutputTokens.HasValue)
        {
            payload["max_tokens"] = request.MaxOutputTokens.Value;
        }

        if (request.ResponseFormat is ResponseFormat.JsonObject)
        {
            payload["response_format"] = new Dictionary<string, object?>
            {
                ["type"] = "json_object"
            };
        }

        var toolsPayload = AzureProviderRequestBuilder.BuildTools(request.Tools);
        if (toolsPayload != null)
        {
            payload["tools"] = toolsPayload;
        }

        if (request.ProviderHints != null)
        {
            foreach (var hint in request.ProviderHints)
            {
                payload[hint.Key] = hint.Value;
            }
        }

        return payload;
    }

    private IDictionary<string, string>? BuildAdditionalHeaders(ProviderCallContext context)
        => BuildAdditionalHeadersInternal(context, streaming: false);

    private IDictionary<string, string>? BuildAdditionalHeadersInternal(ProviderCallContext context, bool streaming)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(context.RequestId))
        {
            headers["x-ms-client-request-id"] = context.RequestId;
        }

        if (streaming)
        {
            headers["Accept"] = "text/event-stream";
            headers["Cache-Control"] = "no-cache";
        }

        return headers.Count == 0 ? null : headers;
    }

    private async Task<string?> AcquireBearerTokenAsync(CancellationToken cancellationToken)
    {
        if (_options.Credential == null)
        {
            return null;
        }

        var scope = string.IsNullOrWhiteSpace(_options.TokenScope)
            ? AzureProviderDefaults.DefaultAzureOpenAIScope
            : _options.TokenScope;

        var tokenContext = new TokenRequestContext(new[] { scope });
        var accessToken = await _options.Credential.GetTokenAsync(tokenContext, cancellationToken).ConfigureAwait(false);
        return accessToken.Token;
    }

    private Response MapTransportResponse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ProviderUnknownException("Azure OpenAI transport returned an empty response body.");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (!root.TryGetProperty("choices", out var choicesElement) || choicesElement.ValueKind != JsonValueKind.Array || choicesElement.GetArrayLength() == 0)
        {
            throw new ProviderUnknownException("Azure OpenAI response did not contain any choices.");
        }

        var choice = choicesElement[0];

        if (!choice.TryGetProperty("message", out var messageElement))
        {
            throw new ProviderUnknownException("Azure OpenAI response choice did not contain a message payload.");
        }

        var text = ExtractTransportText(messageElement);
        var message = new Message(MessageRole.Assistant, text);
        var usage = MapTransportUsage(root);

        string? finishReason = null;
        if (choice.TryGetProperty("finish_reason", out var finishElement) && finishElement.ValueKind == JsonValueKind.String)
        {
            finishReason = finishElement.GetString();
        }

        var response = new Response(message, usage)
        {
            FinishReason = MapFinishReason(finishReason),
            ToolCalls = MapTransportToolCalls(messageElement),
            ProviderRaw = BuildTransportProviderRaw(root)
        };

        return response;
    }

    private static string ExtractTransportText(JsonElement messageElement)
    {
        if (!messageElement.TryGetProperty("content", out var contentElement))
        {
            return string.Empty;
        }

        return contentElement.ValueKind switch
        {
            JsonValueKind.String => contentElement.GetString() ?? string.Empty,
            JsonValueKind.Array => ConcatenateContentParts(contentElement),
            _ => string.Empty
        };
    }

    private static string ConcatenateContentParts(JsonElement contentElement)
    {
        var builder = new StringBuilder();

        foreach (var part in contentElement.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String)
            {
                builder.Append(part.GetString());
            }
            else if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
            {
                builder.Append(textElement.GetString());
            }
        }

        return builder.ToString();
    }

    private IEnumerable<StreamEvent> ParseStreamingResponse(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            yield break;
        }

        Usage? lastUsage = null;

        foreach (var payload in ReadServerSentEvents(responseBody))
        {
            if (string.Equals(payload, "[DONE]", StringComparison.Ordinal))
            {
                yield return new StreamEvent(StreamEventKind.Complete)
                {
                    UsageDelta = lastUsage,
                    IsTerminal = true
                };
                yield break;
            }

            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            var text = ExtractStreamingDelta(root);
            if (!string.IsNullOrEmpty(text))
            {
                yield return new StreamEvent(StreamEventKind.Delta)
                {
                    TextDelta = text
                };
            }

            var usage = TryMapUsage(root);
            if (usage != null)
            {
                lastUsage = usage;
            }
        }

        yield return new StreamEvent(StreamEventKind.Complete)
        {
            UsageDelta = lastUsage,
            IsTerminal = true
        };
    }

    private static IEnumerable<string> ReadServerSentEvents(string responseBody)
    {
        using var reader = new StringReader(responseBody);
        var builder = new StringBuilder();
        string? line;

        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0)
            {
                if (builder.Length > 0)
                {
                    yield return builder.ToString();
                    builder.Clear();
                }

                continue;
            }

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var data = line.Substring("data:".Length).TrimStart();

            if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
            {
                if (builder.Length > 0)
                {
                    yield return builder.ToString();
                    builder.Clear();
                }

                yield return "[DONE]";
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(data);
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString();
        }
    }

    private static string ExtractStreamingDelta(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choicesElement) || choicesElement.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        foreach (var choice in choicesElement.EnumerateArray())
        {
            if (!choice.TryGetProperty("delta", out var deltaElement) || deltaElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!deltaElement.TryGetProperty("content", out var contentElement))
            {
                continue;
            }

            switch (contentElement.ValueKind)
            {
                case JsonValueKind.String:
                    builder.Append(contentElement.GetString());
                    break;
                case JsonValueKind.Array:
                    foreach (var part in contentElement.EnumerateArray())
                    {
                        if (part.ValueKind == JsonValueKind.String)
                        {
                            builder.Append(part.GetString());
                        }
                        else if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
                        {
                            builder.Append(textElement.GetString());
                        }
                    }
                    break;
            }
        }

        return builder.ToString();
    }

    private static Usage? TryMapUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usageElement) || usageElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return MapUsageElement(usageElement);
    }

    private static Usage MapTransportUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usageElement) || usageElement.ValueKind != JsonValueKind.Object)
        {
            return new Usage(0, 0, 0);
        }

        return MapUsageElement(usageElement);
    }

    private static Usage MapUsageElement(JsonElement usageElement)
    {
        static int ReadInt(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var valueElement) && valueElement.ValueKind == JsonValueKind.Number
                ? valueElement.GetInt32()
                : 0;
        }

        var promptTokens = ReadInt(usageElement, "prompt_tokens");
        var completionTokens = ReadInt(usageElement, "completion_tokens");
        var totalTokens = ReadInt(usageElement, "total_tokens");

        if (totalTokens == 0)
        {
            totalTokens = promptTokens + completionTokens;
        }

        return new Usage(promptTokens, completionTokens, totalTokens);
    }

    private static IReadOnlyList<ToolCall>? MapTransportToolCalls(JsonElement messageElement)
    {
        if (!messageElement.TryGetProperty("tool_calls", out var toolCallsElement) || toolCallsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var toolCalls = new List<ToolCall>();

        foreach (var item in toolCallsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!item.TryGetProperty("function", out var functionElement) || functionElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = functionElement.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()
                : null;

            var arguments = functionElement.TryGetProperty("arguments", out var argumentsElement)
                ? argumentsElement.GetRawText()
                : string.Empty;

            if (!string.IsNullOrEmpty(name))
            {
                toolCalls.Add(new ToolCall(name!, arguments));
            }
        }

        return toolCalls.Count == 0 ? null : toolCalls;
    }

    private static IReadOnlyDictionary<string, object>? BuildTransportProviderRaw(JsonElement root)
    {
        var raw = new Dictionary<string, object>();

        if (root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String)
        {
            raw["id"] = idElement.GetString()!;
        }

        if (root.TryGetProperty("model", out var modelElement) && modelElement.ValueKind == JsonValueKind.String)
        {
            raw["model"] = modelElement.GetString()!;
        }

        if (root.TryGetProperty("created", out var createdElement) && createdElement.ValueKind == JsonValueKind.Number)
        {
            raw["created"] = createdElement.GetInt64();
        }

        return raw.Count == 0 ? null : raw;
    }

    private Exception MapTransportError(int statusCode, string body, string? requestId)
    {
        var message = $"Azure OpenAI request failed with status code {statusCode}.";
        string? providerCode = null;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("error", out var errorElement))
                {
                    if (errorElement.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
                    {
                        message = messageElement.GetString() ?? message;
                    }

                    if (errorElement.TryGetProperty("code", out var codeElement))
                    {
                        providerCode = codeElement.ValueKind switch
                        {
                            JsonValueKind.String => codeElement.GetString(),
                            JsonValueKind.Number => codeElement.GetDouble().ToString(CultureInfo.InvariantCulture),
                            _ => providerCode
                        };
                    }
                }
            }
            catch (JsonException)
            {
                // Ignore parsing failures and fall back to the default message.
            }
        }

        return ProviderErrorMapper.Map(statusCode, message, requestId, providerCode);
    }

    private static FinishReason? MapFinishReason(string? finishReason)
    {
        return finishReason?.ToLowerInvariant() switch
        {
            "stop" => FinishReason.Stop,
            "length" => FinishReason.Length,
            "tool_call" or "tool_calls" => FinishReason.ToolCall,
            _ => null
        };
    }
}
