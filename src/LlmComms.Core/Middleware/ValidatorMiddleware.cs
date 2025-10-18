using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LlmComms.Abstractions.Contracts;
using LlmComms.Abstractions.Exceptions;
using LlmComms.Abstractions.Ports;

namespace LlmComms.Core.Middleware;

/// <summary>
/// Middleware that validates requests and responses for schema compliance.
/// </summary>
public sealed class ValidatorMiddleware : IMiddleware
{
    private const string JsonInvalidFlag = "json_invalid";
    private const string ToolMismatchFlag = "tool_mismatch";
    private const string StreamingToolMismatchFlag = "llm.validation.tool_mismatch";
    private const string StreamingJsonInvalidFlag = "llm.validation.json_invalid";

    /// <inheritdoc />
    public async Task<Response> InvokeAsync(
        LLMContext context,
        Func<LLMContext, Task<Response>> next)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        if (next == null)
            throw new ArgumentNullException(nameof(next));

        var response = await next(context).ConfigureAwait(false);

        ValidateJsonResponse(context, response);
        ValidateToolCalls(context, response.ToolCalls, response);

        return response;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamEvent> InvokeStreamAsync(
        LLMContext context,
        Func<LLMContext, IAsyncEnumerable<StreamEvent>> next)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        if (next == null)
            throw new ArgumentNullException(nameof(next));

        var shouldValidateJson = ShouldValidateJson(context);
        var jsonBuffer = shouldValidateJson ? new StringBuilder() : null;
        var toolDefinitions = BuildToolDefinitionMap(context.Request.Tools);
        var strictMode = context.Options.ThrowOnInvalidJson;

        await foreach (var streamEvent in next(context).ConfigureAwait(false))
        {
            if (jsonBuffer != null && streamEvent.TextDelta != null)
                jsonBuffer.Append(streamEvent.TextDelta);

            if (toolDefinitions != null && streamEvent.ToolCallDelta != null)
            {
                if (!ValidateToolCall(toolDefinitions, streamEvent.ToolCallDelta, strictMode, context.CallContext.RequestId, out var mismatchReason))
                {
                    if (strictMode)
                        throw new ValidationException(mismatchReason!, context.CallContext.RequestId);

                    context.CallContext.Items[StreamingToolMismatchFlag] = true;
                }
            }

            if (jsonBuffer != null && streamEvent.IsTerminal)
            {
                var jsonContent = jsonBuffer.ToString();
                if (!IsJsonObject(jsonContent))
                {
                    if (strictMode)
                        throw new ValidationException("Response does not contain valid JSON while ResponseFormat=json_object.", context.CallContext.RequestId);

                    context.CallContext.Items[StreamingJsonInvalidFlag] = true;
                }
            }

            yield return streamEvent;
        }
    }

    private static void ValidateJsonResponse(LLMContext context, Response response)
    {
        if (!ShouldValidateJson(context))
            return;

        var content = response.Output?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            if (context.Options.ThrowOnInvalidJson)
                throw new ValidationException("Response content is empty while ResponseFormat=json_object.", context.CallContext.RequestId);

            SetProviderRawFlag(response, JsonInvalidFlag, true);
            return;
        }

        if (!IsJsonObject(content!))
        {
            if (context.Options.ThrowOnInvalidJson)
                throw new ValidationException("Response does not contain valid JSON while ResponseFormat=json_object.", context.CallContext.RequestId);

            SetProviderRawFlag(response, JsonInvalidFlag, true);
        }
    }

    private static void ValidateToolCalls(LLMContext context, IReadOnlyList<ToolCall>? toolCalls, Response response)
    {
        if (toolCalls == null || toolCalls.Count == 0)
            return;

        var toolDefinitions = BuildToolDefinitionMap(context.Request.Tools);
        var strictMode = context.Options.ThrowOnInvalidJson;

        foreach (var toolCall in toolCalls)
        {
            if (!ValidateToolCall(toolDefinitions, toolCall, strictMode, context.CallContext.RequestId, out var mismatchReason))
            {
                if (strictMode)
                    throw new ValidationException(mismatchReason!, context.CallContext.RequestId);

                SetProviderRawFlag(response, ToolMismatchFlag, true);
            }
        }
    }

    private static bool ShouldValidateJson(LLMContext context)
    {
        return context.Request.ResponseFormat == ResponseFormat.JsonObject;
    }

    private static bool ValidateToolCall(
        IReadOnlyDictionary<string, ToolDefinition>? definitions,
        ToolCall toolCall,
        bool strictMode,
        string requestId,
        out string? mismatchReason)
    {
        mismatchReason = null;

        if (definitions == null)
        {
            mismatchReason = $"Tool call '{toolCall.Name}' received but no tools were declared.";
            return false;
        }

        if (!definitions.TryGetValue(toolCall.Name, out var definition))
        {
            mismatchReason = $"Tool call '{toolCall.Name}' is not part of the declared tool collection.";
            return false;
        }

        if (!IsValidJson(toolCall.ArgumentsJson))
        {
            mismatchReason = $"Tool call '{toolCall.Name}' arguments are not valid JSON.";
            return false;
        }

        if (!ValidateSchema(definition, toolCall.ArgumentsJson, out mismatchReason))
        {
            return false;
        }

        return true;
    }

    private static IReadOnlyDictionary<string, ToolDefinition>? BuildToolDefinitionMap(ToolCollection? tools)
    {
        if (tools == null || tools.Tools == null || tools.Tools.Count == 0)
            return null;

        return tools.Tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
    }

    private static bool ValidateSchema(ToolDefinition definition, string json, out string? reason)
    {
        reason = null;

        if (definition.JsonSchema == null || definition.JsonSchema.Count == 0)
            return true;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            foreach (var requiredProperty in ExtractRequiredProperties(definition.JsonSchema))
            {
                if (!root.TryGetProperty(requiredProperty, out _))
                {
                    reason = $"Tool call '{definition.Name}' is missing required argument '{requiredProperty}'.";
                    return false;
                }
            }
        }
        catch (JsonException)
        {
            reason = $"Tool call '{definition.Name}' arguments are not valid JSON.";
            return false;
        }

        return true;
    }

    private static IEnumerable<string> ExtractRequiredProperties(IReadOnlyDictionary<string, object> schema)
    {
        if (!schema.TryGetValue("required", out var required))
            return Array.Empty<string>();

        return required switch
        {
            JsonElement element when element.ValueKind == JsonValueKind.Array => element.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToArray(),
            IEnumerable<string> stringEnumerable => stringEnumerable,
            IEnumerable<object> objectEnumerable => objectEnumerable.OfType<string>().ToArray(),
            _ => Array.Empty<string>()
        };
    }

    private static bool IsJsonObject(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsValidJson(string content)
    {
        try
        {
            JsonDocument.Parse(content);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void SetProviderRawFlag(Response response, string key, object value)
    {
        Dictionary<string, object> mutable;
        if (response.ProviderRaw is Dictionary<string, object> dictionary)
        {
            mutable = dictionary;
        }
        else
        {
            mutable = response.ProviderRaw != null
                ? response.ProviderRaw.ToDictionary(kv => kv.Key, kv => kv.Value)
                : new Dictionary<string, object>();
        }

        mutable[key] = value;
        response.ProviderRaw = mutable;
    }
}
