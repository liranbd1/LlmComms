using System;
using System.ClientModel;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LlmComms.Abstractions.Contracts;
using LlmComms.Abstractions.Exceptions;
using LlmComms.Abstractions.Ports;
using LlmComms.Core.Transport;
using LlmComms.Core.Utilities;
using OpenAI;
using OpenAI.Chat;
using ChatClient = global::OpenAI.Chat.ChatClient;
using TimeoutException = LlmComms.Abstractions.Exceptions.TimeoutException;

namespace LlmComms.Providers.OpenAI;

/// <summary>
/// Provider adapter that integrates the official OpenAI .NET SDK with the LlmComms abstractions.
/// </summary>
public sealed class OpenAIProvider : IProvider
{
    private readonly OpenAIProviderOptions _options;
    private readonly ITransport? _transportOverride;
    private readonly ConcurrentDictionary<string, ChatClient> _chatClients = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAIProvider"/> class using default options.
    /// </summary>
    public OpenAIProvider()
        : this(new OpenAIProviderOptions(), transport: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAIProvider"/> class.
    /// </summary>
    /// <param name="options">Provider configuration that mirrors the official OpenAI SDK.</param>
    public OpenAIProvider(OpenAIProviderOptions options)
        : this(options, transport: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAIProvider"/> class with a custom transport.
    /// </summary>
    /// <param name="options">Provider configuration that mirrors the official OpenAI SDK.</param>
    /// <param name="transport">Custom transport implementation; when <c>null</c> the provider uses the SDK.</param>
    public OpenAIProvider(OpenAIProviderOptions options, ITransport? transport)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _transportOverride = transport ?? options.Transport;
    }

    /// <inheritdoc />
    public string Name => "openai";

    /// <inheritdoc />
    public IModel CreateModel(string modelId, ProviderModelOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("Model identifier must be provided.", nameof(modelId));

        _ = options; // Reserved for future OpenAI-specific model configuration.

        return new OpenAIModel(modelId);
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

    private Task<Response> SendWithSdkAsync(
        IModel model,
        Request request,
        ProviderCallContext context,
        CancellationToken cancellationToken)
    {
        var targetModelId = ResolveModelId(model);
        var chatClient = GetChatClient(targetModelId);

        var messages = BuildChatMessages(request);
        var options = BuildCompletionOptions(request);

        return ExecuteChatCompletionAsync(chatClient, messages, options, context, cancellationToken);
    }

    private Task<Response> SendWithTransportAsync(
        IModel model,
        Request request,
        ProviderCallContext context,
        CancellationToken cancellationToken)
    {
        var transportRequest = BuildTransportRequest(model, request);

        return SendWithTransportCoreAsync(transportRequest, context, cancellationToken);
    }

    private async IAsyncEnumerable<StreamEvent> StreamWithSdkAsync(
        IModel model,
        Request request,
        ProviderCallContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var targetModelId = ResolveModelId(model);
        var chatClient = GetChatClient(targetModelId);
        var messages = BuildChatMessages(request);
        var options = BuildCompletionOptions(request);

        Usage? lastUsage = null;

        IAsyncEnumerable<StreamingChatCompletionUpdate> updates;

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

    private async Task<Response> ExecuteChatCompletionAsync(
        ChatClient chatClient,
        IReadOnlyList<ChatMessage> messages,
        ChatCompletionOptions options,
        ProviderCallContext context,
        CancellationToken cancellationToken)
    {
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

    private async Task<Response> SendWithTransportCoreAsync(
        HttpTransportRequest transportRequest,
        ProviderCallContext context,
        CancellationToken cancellationToken)
    {
        var transportResponse = await _transportOverride!.SendAsync(transportRequest, cancellationToken).ConfigureAwait(false);
        var (statusCode, body) = TransportResponseReader.Read(transportResponse);

        if (statusCode >= 400)
        {
            throw MapTransportError(statusCode, body, context.RequestId);
        }

        return MapTransportResponse(body);
    }

    private HttpTransportRequest BuildTransportRequest(IModel model, Request request)
    {
        var modelId = ResolveModelId(model);
        var baseUri = _options.Endpoint ?? new Uri("https://api.openai.com/");
        var apiUri = new Uri(baseUri, "/v1/chat/completions");

        var payload = new Dictionary<string, object?>
        {
            ["model"] = modelId,
            ["messages"] = BuildTransportMessages(request.Messages)
        };

        if (request.Temperature.HasValue)
            payload["temperature"] = request.Temperature.Value;

        if (request.TopP.HasValue)
            payload["top_p"] = request.TopP.Value;

        if (request.MaxOutputTokens.HasValue)
            payload["max_tokens"] = request.MaxOutputTokens.Value;

        if (request.ResponseFormat is ResponseFormat.JsonObject)
        {
            payload["response_format"] = new Dictionary<string, object?>
            {
                ["type"] = "json_object"
            };
        }

        var tools = BuildTransportTools(request.Tools);
        if (tools != null)
            payload["tools"] = tools;

        if (request.ProviderHints != null)
        {
            foreach (var hint in request.ProviderHints)
            {
                payload[hint.Key] = hint.Value;
            }
        }

        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var body = JsonSerializer.Serialize(payload, serializerOptions);

        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "application/json"
        };

        var apiKey = ResolveApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            headers["Authorization"] = $"Bearer {apiKey}";
        }

        if (!string.IsNullOrWhiteSpace(_options.Organization))
        {
            headers["OpenAI-Organization"] = _options.Organization!;
        }

        return new HttpTransportRequest
        {
            Url = apiUri.ToString(),
            Method = "POST",
            Headers = headers,
            Body = body
        };
    }

    private Response MapTransportResponse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ProviderUnknownException("OpenAI transport returned an empty response body.");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (!root.TryGetProperty("choices", out var choicesElement) || choicesElement.ValueKind != JsonValueKind.Array || choicesElement.GetArrayLength() == 0)
        {
            throw new ProviderUnknownException("OpenAI response did not contain any choices.");
        }

        var choice = choicesElement[0];
        var messageElement = choice.GetProperty("message");

        var text = ExtractTransportText(messageElement);
        var message = new Message(MessageRole.Assistant, text);

        var usage = MapTransportUsage(root);

        var response = new Response(message, usage)
        {
            FinishReason = MapFinishReason(choice.TryGetProperty("finish_reason", out var finishElement) && finishElement.ValueKind == JsonValueKind.String
                ? finishElement.GetString()
                : null),
            ToolCalls = MapTransportToolCalls(messageElement),
            ProviderRaw = BuildProviderRaw(root)
        };

        return response;
    }

    private static string ExtractTransportText(JsonElement messageElement)
    {
        if (!messageElement.TryGetProperty("content", out var contentElement))
            return string.Empty;

        return contentElement.ValueKind switch
        {
            JsonValueKind.String => contentElement.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Concat(contentElement
                .EnumerateArray()
                .Select(part => part.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String
                    ? textElement.GetString()
                    : string.Empty)),
            _ => string.Empty
        };
    }

    private static Usage MapTransportUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usageElement) || usageElement.ValueKind != JsonValueKind.Object)
        {
            return new Usage(0, 0, 0);
        }

        int TryGetInt(string propertyName)
        {
            return usageElement.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number
                ? property.GetInt32()
                : 0;
        }

        var prompt = TryGetInt("prompt_tokens");
        var completion = TryGetInt("completion_tokens");
        var total = TryGetInt("total_tokens");

        return new Usage(prompt, completion, total == 0 ? prompt + completion : total);
    }

    private static IReadOnlyList<ToolCall>? MapTransportToolCalls(JsonElement messageElement)
    {
        if (!messageElement.TryGetProperty("tool_calls", out var toolCallsElement) || toolCallsElement.ValueKind != JsonValueKind.Array)
            return null;

        var toolCalls = new List<ToolCall>();

        foreach (var item in toolCallsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            if (!item.TryGetProperty("function", out var functionElement) || functionElement.ValueKind != JsonValueKind.Object)
                continue;

            var name = functionElement.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()
                : null;

            var arguments = functionElement.TryGetProperty("arguments", out var argumentsElement)
                ? argumentsElement.GetRawText()
                : string.Empty;

            if (!string.IsNullOrEmpty(name))
            {
                toolCalls.Add(new ToolCall(name!, arguments ?? string.Empty));
            }
        }

        return toolCalls.Count == 0 ? null : toolCalls;
    }

    private IReadOnlyDictionary<string, object>? BuildProviderRaw(JsonElement root)
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
        string message = $"OpenAI request failed with status code {statusCode}.";
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

    private static IList<Dictionary<string, object?>> BuildTransportMessages(IReadOnlyList<Message> messages)
    {
        var result = new List<Dictionary<string, object?>>(messages.Count);

        foreach (var message in messages)
        {
            var payload = new Dictionary<string, object?>
            {
                ["role"] = MessageRoleMapper.ToString(message.Role),
                ["content"] = message.Content
            };

            result.Add(payload);
        }

        return result;
    }

    private static IList<Dictionary<string, object?>>? BuildTransportTools(ToolCollection? toolCollection)
    {
        var descriptors = FunctionToolDescriptorFactory.CreateDescriptors(toolCollection);
        if (descriptors.Count == 0)
            return null;

        var tools = new List<Dictionary<string, object?>>(descriptors.Count);

        foreach (var descriptor in descriptors)
        {
            var toolPayload = new Dictionary<string, object?>
            {
                ["type"] = "function",
                ["function"] = new Dictionary<string, object?>
                {
                    ["name"] = descriptor.Name,
                    ["description"] = descriptor.Description,
                    ["parameters"] = descriptor.Parameters
                }
            };

            tools.Add(toolPayload);
        }

        return tools;
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

    private IAsyncEnumerable<StreamEvent> StreamWithTransportAsync(
        IModel model,
        Request request,
        ProviderCallContext context,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Streaming is not supported when a custom transport is supplied to OpenAIProvider.");
    }

    /// <summary>
    /// Retrieves (or lazily creates) the <see cref="ChatClient"/> bound to the supplied model identifier.
    /// </summary>
    private ChatClient GetChatClient(string modelId)
    {
        return _chatClients.GetOrAdd(modelId, CreateChatClient);
    }

    /// <summary>
    /// Factory invoked by <see cref="ConcurrentDictionary{TKey,TValue}"/> to build a chat client for a model.
    /// </summary>
    private ChatClient CreateChatClient(string modelId)
    {
        if (_options.ChatClientFactory != null)
        {
            return _options.ChatClientFactory.Invoke(modelId);
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OpenAI API key must be provided via options or environment variable 'OPENAI_API_KEY'.");

        var credential = new ApiKeyCredential(apiKey!);
        var clientOptions = new OpenAIClientOptions();

        if (_options.Endpoint != null)
        {
            clientOptions.Endpoint = _options.Endpoint;
        }

        _options.ConfigureClientOptions?.Invoke(clientOptions);

        return new ChatClient(modelId, credential, clientOptions);
    }

    /// <summary>
    /// Resolves the API key, preferring explicit configuration and falling back to environment variables.
    /// </summary>
    private string? ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            return _options.ApiKey;

        return Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    }

    private string ResolveModelId(IModel model)
    {
        if (!string.IsNullOrWhiteSpace(model.ModelId))
        {
            return model.ModelId;
        }

        if (!string.IsNullOrWhiteSpace(_options.DefaultModelId))
        {
            return _options.DefaultModelId;
        }

        throw new InvalidOperationException("No model identifier provided. Configure a model on the provider options or specify one when creating the model instance.");
    }

    /// <summary>
    /// Converts the request transcript into the SDK-specific chat message hierarchy.
    /// </summary>
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
                    // Our abstraction does not propagate tool call identifiers; synthesize one so the
                    // SDK recognises the message as tool output.
                    messages.Add(new ToolChatMessage(Guid.NewGuid().ToString("N"), message.Content));
                    break;
                default:
                    messages.Add(new UserChatMessage(message.Content));
                    break;
            }
        }

        return messages;
    }

    /// <summary>
    /// Translates generic request options into <see cref="ChatCompletionOptions"/> consumed by the SDK.
    /// </summary>
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

    /// <summary>
    /// Converts an abstract tool definition into the SDK function-call representation.
    /// </summary>
    private static ChatTool ConvertTool(ToolDefinition tool)
    {
        BinaryData? schemaPayload = null;

        if (tool.JsonSchema.Count > 0)
        {
            var serialized = JsonSerializer.Serialize(tool.JsonSchema);
            schemaPayload = BinaryData.FromString(serialized);
        }

        return ChatTool.CreateFunctionTool(
            functionName: tool.Name,
            functionDescription: tool.Description,
            functionParameters: schemaPayload);
    }
}
