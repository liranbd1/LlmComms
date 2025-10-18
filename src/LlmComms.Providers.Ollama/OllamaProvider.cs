using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LlmComms.Abstractions.Contracts;
using LlmComms.Abstractions.Exceptions;
using LlmComms.Abstractions.Ports;
using LlmComms.Core.Transport;

namespace LlmComms.Providers.Ollama;

/// <summary>
/// Provider implementation targeting the Ollama REST API.
/// </summary>
public sealed class OllamaProvider : IProvider
{
    private readonly Uri _baseUri;
    private readonly ITransport _transport;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public OllamaProvider()
        : this(new OllamaProviderOptions(), transport: null)
    {
    }

    public OllamaProvider(OllamaProviderOptions options)
        : this(options, transport: null)
    {
    }

    public OllamaProvider(OllamaProviderOptions options, ITransport? transport)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        _baseUri = options.BaseUrl ?? throw new ArgumentNullException(nameof(options.BaseUrl));
        _transport = transport ?? new HttpClientTransport();
    }

    public string Name => "ollama";

    public IModel CreateModel(string modelId, ProviderModelOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("Model identifier must be provided.", nameof(modelId));

        return new OllamaModel(modelId, "chat");
    }

    public async Task<Response> SendAsync(IModel model, Request request, ProviderCallContext context, CancellationToken cancellationToken)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        var payload = BuildChatRequestPayload(model, request, stream: false);
        var response = await SendChatRequestAsync(payload, context, cancellationToken).ConfigureAwait(false);

        return MapChatResponse(response);
    }

    public IAsyncEnumerable<StreamEvent> StreamAsync(IModel model, Request request, ProviderCallContext context, CancellationToken cancellationToken)
    {
        return StreamAsyncImpl(model, request, context, cancellationToken);
    }

    private async IAsyncEnumerable<StreamEvent> StreamAsyncImpl(IModel model, Request request, ProviderCallContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        var payload = BuildChatRequestPayload(model, request, stream: true);
        var rawStream = await SendChatStreamingAsync(payload, context, cancellationToken).ConfigureAwait(false);

        var emittedTerminal = false;
        var reasoningSegments = new List<ReasoningSegment>();

        foreach (var chunk in ParseStreamingPayloads(rawStream, context))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryGetError(chunk, out var errorMessage))
                throw new ProviderUnknownException(errorMessage, context.RequestId);

            var messagePayload = chunk.Message;
            if (!string.IsNullOrEmpty(messagePayload?.Content))
            {
                var text = messagePayload!.Content;
                yield return new StreamEvent(StreamEventKind.Delta)
                {
                    TextDelta = text
                };
            }

            if (!string.IsNullOrEmpty(messagePayload?.Thinking))
            {
                var reasoningSegment = new ReasoningSegment
                {
                    Text = messagePayload!.Thinking!
                };
                reasoningSegments.Add(reasoningSegment);
                yield return new StreamEvent(StreamEventKind.Reasoning)
                {
                    ReasoningDelta = reasoningSegment
                };
            }

            if (messagePayload?.ToolCalls is { Count: > 0 } toolCalls)
            {
                var mappedCalls = MapToolCalls(toolCalls);
                if (mappedCalls != null)
                {
                    foreach (var toolCall in mappedCalls)
                    {
                        yield return new StreamEvent(StreamEventKind.ToolCall)
                        {
                            ToolCallDelta = toolCall
                        };
                    }
                }
            }

            if (chunk.Done)
            {
                emittedTerminal = true;
                var promptCount = chunk.PromptEvalCount ?? 0;
                var completionCount = chunk.EvalCount ?? 0;
                var usage = new Usage(promptCount, completionCount, promptCount + completionCount);

                yield return new StreamEvent(StreamEventKind.Complete)
                {
                    UsageDelta = usage,
                    ReasoningDelta = reasoningSegments.Count > 0 ? new ReasoningSegment
                    {
                        Text = string.Concat(reasoningSegments.Select(s => s.Text))
                    } : null,
                    IsTerminal = true
                };
            }
        }

        if (!emittedTerminal)
        {
            yield return new StreamEvent(StreamEventKind.Complete)
            {
                IsTerminal = true
            };
        }
    }

    private OllamaApi.ChatRequestPayload BuildChatRequestPayload(IModel model, Request request, bool stream)
    {
        var payload = new OllamaApi.ChatRequestPayload
        {
            Model = model.ModelId,
            Messages = MapMessages(request.Messages),
            Tools = MapTools(request.Tools),
            Options = BuildOptions(request),
            Stream = stream,
            Format = request.ResponseFormat == ResponseFormat.JsonObject ? "json" : ExtractFormatHint(request),
            KeepAlive = ExtractKeepAliveHint(request)
        };

        return payload;
    }

    private async Task<OllamaApi.ChatResponsePayload> SendChatRequestAsync(OllamaApi.ChatRequestPayload payload, ProviderCallContext context, CancellationToken cancellationToken)
    {
        var requestBody = JsonSerializer.Serialize(payload, _serializerOptions);

        var transportRequest = new TransportRequest
        {
            Url = new Uri(_baseUri, "/api/chat").ToString(),
            Method = "POST",
            Headers = new Dictionary<string, string>
            {
                {"Content-Type", "application/json"}
            },
            Body = requestBody
        };

        var transportResponse = await _transport.SendAsync(transportRequest, cancellationToken).ConfigureAwait(false);
        var (statusCode, body) = ExtractTransportResponse(transportResponse);

        if (statusCode >= 400)
        {
            var errorMessage = TryExtractErrorMessage(body) ?? $"Ollama request failed with status code {statusCode}.";
            throw new ProviderUnavailableException(errorMessage, context.RequestId, statusCode);
        }

        try
        {
            var payloadResponse = JsonSerializer.Deserialize<OllamaApi.ChatResponsePayload>(body, _serializerOptions);
            if (payloadResponse == null)
                throw new ProviderUnknownException("Received empty response from Ollama.", context.RequestId);

            return payloadResponse;
        }
        catch (JsonException ex)
        {
            var fallback = TryParseConcatenatedResponses(body);
            if (fallback != null)
                return fallback;

            throw new ProviderUnknownException("Failed to parse Ollama response.", context.RequestId, innerException: ex);
        }
    }

    private OllamaApi.ChatResponsePayload? TryParseConcatenatedResponses(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        var bytes = Encoding.UTF8.GetBytes(body);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { AllowTrailingCommas = true });

        OllamaApi.ChatResponsePayload? last = null;
        OllamaApi.ChatResponsePayload? lastWithMessage = null;

        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                continue;

            try
            {
                using var document = JsonDocument.ParseValue(ref reader);
                var json = document.RootElement.GetRawText();
                var parsed = JsonSerializer.Deserialize<OllamaApi.ChatResponsePayload>(json, _serializerOptions);
                if (parsed != null)
                {
                    last = parsed;
                    if (!string.IsNullOrEmpty(parsed.Message?.Content))
                    {
                        lastWithMessage = parsed;
                    }
                }
            }
            catch (JsonException)
            {
                // try remaining content
            }
        }

        return lastWithMessage ?? last;
    }

    private async Task<string> SendChatStreamingAsync(OllamaApi.ChatRequestPayload payload, ProviderCallContext context, CancellationToken cancellationToken)
    {
        var requestBody = JsonSerializer.Serialize(payload, _serializerOptions);

        var transportRequest = new TransportRequest
        {
            Url = new Uri(_baseUri, "/api/chat").ToString(),
            Method = "POST",
            Headers = new Dictionary<string, string>
            {
                {"Content-Type", "application/json"}
            },
            Body = requestBody
        };

        var transportResponse = await _transport.SendAsync(transportRequest, cancellationToken).ConfigureAwait(false);
        var (statusCode, body) = ExtractTransportResponse(transportResponse);

        if (statusCode >= 400)
        {
            var errorMessage = TryExtractErrorMessage(body) ?? $"Ollama streaming request failed with status code {statusCode}.";
            throw new ProviderUnavailableException(errorMessage, context.RequestId, statusCode);
        }

        return body;
    }

    private static (int StatusCode, string Body) ExtractTransportResponse(object transportResponse)
    {
        var type = transportResponse.GetType();

        var statusCodeProperty = type.GetProperty("StatusCode")
            ?? throw new InvalidOperationException("Transport response missing StatusCode property.");
        var bodyProperty = type.GetProperty("Body")
            ?? throw new InvalidOperationException("Transport response missing Body property.");

        var statusCodeValue = statusCodeProperty.GetValue(transportResponse);
        var bodyValue = bodyProperty.GetValue(transportResponse);

        if (statusCodeValue is not int statusCode)
            throw new InvalidOperationException("Transport response StatusCode must be an integer.");

        var body = bodyValue as string ?? string.Empty;

        return (statusCode, body);
    }

    private string? TryExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            var error = JsonSerializer.Deserialize<OllamaApi.ErrorPayload>(body, _serializerOptions);
            return error?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IList<OllamaApi.MessagePayload> MapMessages(IReadOnlyList<Message> messages)
    {
        var result = new List<OllamaApi.MessagePayload>(messages.Count);

        foreach (var message in messages)
        {
            var payload = new OllamaApi.MessagePayload
            {
                Role = MapRole(message.Role),
                Content = message.Content
            };

            result.Add(payload);
        }

        return result;
    }

    private static string MapRole(MessageRole role)
    {
        return role switch
        {
            MessageRole.System => "system",
            MessageRole.User => "user",
            MessageRole.Assistant => "assistant",
            MessageRole.Function => "tool",
            _ => "user"
        };
    }

    private static IList<OllamaApi.ToolPayload>? MapTools(ToolCollection? toolCollection)
    {
        if (toolCollection == null || toolCollection.Tools.Count == 0)
            return null;

        var tools = new List<OllamaApi.ToolPayload>(toolCollection.Tools.Count);

        foreach (var tool in toolCollection.Tools)
        {
            var parameters = new Dictionary<string, object>();
            foreach (var kvp in tool.JsonSchema)
                parameters[kvp.Key] = kvp.Value;

            var functionPayload = new OllamaApi.ToolFunctionPayload
            {
                Name = tool.Name,
                Description = tool.Description,
                Parameters = parameters
            };

            tools.Add(new OllamaApi.ToolPayload
            {
                Function = functionPayload
            });
        }

        return tools;
    }

    private IDictionary<string, object>? BuildOptions(Request request)
    {
        var options = new Dictionary<string, object>();

        if (request.Temperature.HasValue)
            options["temperature"] = request.Temperature.Value;

        if (request.TopP.HasValue)
            options["top_p"] = request.TopP.Value;

        if (request.MaxOutputTokens.HasValue)
            options["num_predict"] = request.MaxOutputTokens.Value;

        if (request.ProviderHints != null)
            MergeHintOptions(options, request.ProviderHints);

        return options.Count == 0 ? null : options;
    }

    private static object? ExtractFormatHint(Request request)
    {
        if (request.ProviderHints == null)
            return null;

        if (request.ProviderHints.TryGetValue("ollama.format", out var value))
            return value;

        return null;
    }

    private static object? ExtractKeepAliveHint(Request request)
    {
        if (request.ProviderHints == null)
            return null;

        if (!request.ProviderHints.TryGetValue("ollama.keep_alive", out var hint))
            return null;

        if (hint is null or string)
            return hint;

        if (hint is int || hint is long || hint is double)
            return hint;

        return hint?.ToString();
    }

    private static void MergeHintOptions(IDictionary<string, object> options, IReadOnlyDictionary<string, object> hints)
    {
        if (hints.TryGetValue("ollama.options", out var optionsHint))
        {
            if (optionsHint is IReadOnlyDictionary<string, object> readOnlyDict)
            {
                foreach (var pair in readOnlyDict)
                    options[pair.Key] = pair.Value;
            }
            else if (optionsHint is IDictionary<string, object> dict)
            {
                foreach (var pair in dict)
                    options[pair.Key] = pair.Value;
            }
        }
    }

    private Response MapChatResponse(OllamaApi.ChatResponsePayload payload)
    {
        var message = payload.Message;
        var content = message?.Content;

        if (string.IsNullOrEmpty(content) && payload.ExtensionData != null && payload.ExtensionData.TryGetValue("response", out var responseValue) && responseValue is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
        {
            content = jsonElement.GetString();
        }

        var responseMessage = new Message(MessageRole.Assistant, content ?? string.Empty);

        var promptTokens = payload.PromptEvalCount ?? 0;
        var completionTokens = payload.EvalCount ?? 0;
        var usage = new Usage(promptTokens, completionTokens, promptTokens + completionTokens);

        var response = new Response(responseMessage, usage)
        {
            FinishReason = MapFinishReason(payload.DoneReason),
            ToolCalls = MapToolCalls(message?.ToolCalls),
            Reasoning = BuildReasoningTrace(message, payload.ExtensionData)
        };

        var providerRaw = new Dictionary<string, object>();
        if (payload.ExtensionData != null)
        {
            foreach (var kvp in payload.ExtensionData)
                providerRaw[kvp.Key] = kvp.Value;
        }
        providerRaw["prompt_eval_count"] = promptTokens;
        providerRaw["eval_count"] = completionTokens;
        response.ProviderRaw = providerRaw;

        return response;
    }

    private IReadOnlyList<ToolCall>? MapToolCalls(IList<OllamaApi.ToolCallPayload>? toolCalls)
    {
        if (toolCalls == null || toolCalls.Count == 0)
            return null;

        var result = new List<ToolCall>(toolCalls.Count);

        foreach (var toolCall in toolCalls)
        {
            var argumentsJson = JsonSerializer.Serialize(toolCall.Arguments, _serializerOptions);
            result.Add(new ToolCall(toolCall.Name, argumentsJson));
        }

        return result;
    }

    private static FinishReason? MapFinishReason(string? reason)
    {
        return reason switch
        {
            "stop" => FinishReason.Stop,
            "length" => FinishReason.Length,
            "tool" => FinishReason.ToolCall,
            _ => null
        };
    }

    private sealed class TransportRequest
    {
        public string Url { get; set; } = string.Empty;
        public string Method { get; set; } = "POST";
        public IDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
        public string Body { get; set; } = string.Empty;
    }

    private IEnumerable<OllamaApi.ChatResponsePayload> ParseStreamingPayloads(string rawStream, ProviderCallContext context)
    {
        if (string.IsNullOrWhiteSpace(rawStream))
            yield break;

        var segments = rawStream.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            var trimmed = segment.Trim();
            if (trimmed.Length == 0)
                continue;

            OllamaApi.ChatResponsePayload? payload = null;

            try
            {
                payload = JsonSerializer.Deserialize<OllamaApi.ChatResponsePayload>(trimmed, _serializerOptions);
            }
            catch (JsonException)
            {
                var error = JsonSerializer.Deserialize<OllamaApi.ErrorPayload>(trimmed, _serializerOptions);
                if (error != null)
                {
                    var errorMessage = error.Error;
                    if (!string.IsNullOrEmpty(errorMessage))
                        throw new ProviderUnknownException(errorMessage!, context.RequestId);
                }

                throw new ProviderUnknownException("Failed to parse streaming response chunk from Ollama.", context.RequestId);
            }

            if (payload != null)
                yield return payload;
        }
    }

    private static bool TryGetError(OllamaApi.ChatResponsePayload payload, out string errorMessage)
    {
        if (payload.ExtensionData != null &&
            payload.ExtensionData.TryGetValue("error", out var errorValue) &&
            errorValue is string errorString &&
            !string.IsNullOrWhiteSpace(errorString))
        {
            errorMessage = errorString;
            return true;
        }

        errorMessage = string.Empty;
        return false;
    }

    private ReasoningTrace? BuildReasoningTrace(OllamaApi.MessagePayload? message, IDictionary<string, object>? extensionData)
    {
        var segments = new List<ReasoningSegment>();

        if (!string.IsNullOrWhiteSpace(message?.Thinking))
        {
            segments.Add(new ReasoningSegment
            {
                Text = message!.Thinking!
            });
        }

        if (extensionData != null && extensionData.TryGetValue("thinking", out var extraThinking))
        {
            var text = ExtractString(extraThinking);
            if (!string.IsNullOrWhiteSpace(text))
            {
                segments.Add(new ReasoningSegment
                {
                    Text = text!
                });
            }
        }

        if (segments.Count == 0)
            return null;

        IReadOnlyDictionary<string, object>? metadata = null;
        if (extensionData != null && extensionData.TryGetValue("thinking_tokens", out var tokenValue))
        {
            var tokens = ExtractNumber(tokenValue);
            if (tokens.HasValue)
            {
                metadata = new Dictionary<string, object>
                {
                    ["thinking_tokens"] = tokens.Value
                };
            }
        }

        return new ReasoningTrace
        {
            Segments = segments,
            Metadata = metadata
        };
    }

    private static string? ExtractString(object? value)
    {
        return value switch
        {
            null => null,
            string s => s,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            JsonElement element when element.ValueKind == JsonValueKind.Object || element.ValueKind == JsonValueKind.Array => element.GetRawText(),
            _ => value?.ToString()
        };
    }

    private static long? ExtractNumber(object? value)
    {
        return value switch
        {
            null => null,
            long l => l,
            int i => i,
            double d => (long)d,
            JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var result) => result,
            _ => null
        };
    }
}
