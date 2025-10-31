using System;
using System.Collections.Generic;
using System.Linq;
using LlmComms.Abstractions.Contracts;
using LlmComms.Core.Utilities;

namespace LlmComms.Core.Utilities.Azure;

/// <summary>
/// Shared helpers for shaping Azure provider HTTP payloads and headers.
/// </summary>
internal static class AzureProviderRequestBuilder
{
    public static Uri BuildOpenAIEndpoint(string resourceName, string path, string apiVersion)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
            throw new ArgumentException("Resource name must be provided.", nameof(resourceName));
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must be provided.", nameof(path));
        if (string.IsNullOrWhiteSpace(apiVersion))
            throw new ArgumentException("API version must be provided.", nameof(apiVersion));

        var baseUri = $"https://{resourceName}.openai.azure.com";
        var normalizedPath = path.StartsWith("/", StringComparison.Ordinal) ? path : $"/{path}";

        var builder = new UriBuilder(baseUri)
        {
            Path = normalizedPath
        };

        var query = $"api-version={Uri.EscapeDataString(apiVersion)}";
        builder.Query = string.IsNullOrEmpty(builder.Query)
            ? query
            : $"{builder.Query.TrimStart('?')}&{query}";

        return builder.Uri;
    }

    public static Uri BuildInferenceEndpoint(Uri baseEndpoint, string path, string apiVersion)
    {
        if (baseEndpoint == null)
            throw new ArgumentNullException(nameof(baseEndpoint));
        if (!baseEndpoint.IsAbsoluteUri)
            throw new ArgumentException("Endpoint must be absolute.", nameof(baseEndpoint));
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must be provided.", nameof(path));
        if (string.IsNullOrWhiteSpace(apiVersion))
            throw new ArgumentException("API version must be provided.", nameof(apiVersion));

        var normalizedPath = path.StartsWith("/", StringComparison.Ordinal) ? path : $"/{path}";

        var builder = new UriBuilder(baseEndpoint)
        {
            Path = normalizedPath
        };

        var query = $"api-version={Uri.EscapeDataString(apiVersion)}";
        builder.Query = string.IsNullOrEmpty(builder.Query)
            ? query
            : $"{builder.Query.TrimStart('?')}&{query}";

        return builder.Uri;
    }

    public static IDictionary<string, string> BuildHeaders(
        string? apiKey,
        string? bearerToken,
        IDictionary<string, string>? additionalHeaders = null)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = "application/json"
        };

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            headers["api-key"] = apiKey!;
        }

        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            headers["Authorization"] = $"Bearer {bearerToken}";
        }

        if (additionalHeaders != null)
        {
            foreach (var kvp in additionalHeaders)
            {
                headers[kvp.Key] = kvp.Value;
            }
        }

        return headers;
    }

    public static IReadOnlyList<object> BuildMessages(IReadOnlyList<Message> messages)
    {
        if (messages == null)
            throw new ArgumentNullException(nameof(messages));

        if (messages.Count == 0)
            return Array.Empty<object>();

        var payload = new List<object>(messages.Count);

        foreach (var message in messages)
        {
            var role = MessageRoleMapper.ToString(message.Role);
            payload.Add(new
            {
                role,
                content = message.Content
            });
        }

        return payload;
    }

    public static object? BuildTools(ToolCollection? tools)
    {
        if (tools == null || tools.Tools.Count == 0)
            return null;

        var descriptors = FunctionToolDescriptorFactory.CreateDescriptors(tools);
        if (descriptors.Count == 0)
            return null;

        return descriptors.Select(d => new
        {
            type = "function",
            function = new
            {
                name = d.Name,
                description = d.Description,
                parameters = d.Parameters
            }
        }).ToArray();
    }
}
