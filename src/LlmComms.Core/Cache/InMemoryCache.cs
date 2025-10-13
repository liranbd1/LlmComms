using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LlmComms.Abstractions.Contracts;
using LlmComms.Abstractions.Ports;

namespace LlmComms.Core.Cache;

/// <summary>
/// Simple in-memory implementation of <see cref="ILLMCache"/> for single-process scenarios.
/// </summary>
public sealed class InMemoryCache : ILLMCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

    /// <inheritdoc />
    public Task<Response?> GetAsync(string key, CancellationToken cancellationToken)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        cancellationToken.ThrowIfCancellationRequested();

        if (_entries.TryGetValue(key, out var entry))
        {
            if (entry.IsExpired)
            {
                _entries.TryRemove(key, out _);
                return Task.FromResult<Response?>(null);
            }

            return Task.FromResult<Response?>(CloneResponse(entry.Response));
        }

        return Task.FromResult<Response?>(null);
    }

    /// <inheritdoc />
    public Task SetAsync(string key, Response response, TimeSpan ttl, CancellationToken cancellationToken)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));
        if (response == null)
            throw new ArgumentNullException(nameof(response));
        if (ttl <= TimeSpan.Zero)
            return Task.CompletedTask;

        cancellationToken.ThrowIfCancellationRequested();

        var expiresAt = DateTimeOffset.UtcNow.Add(ttl);
        var entry = new CacheEntry(CloneResponse(response), expiresAt);
        _entries[key] = entry;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        cancellationToken.ThrowIfCancellationRequested();

        _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    private static Response CloneResponse(Response response)
    {
        var clonedUsage = response.Usage != null
            ? new Usage(response.Usage.PromptTokens, response.Usage.CompletionTokens, response.Usage.TotalTokens)
            : new Usage(0, 0, 0);

        var clonedMessage = response.Output != null
            ? new Message(response.Output.Role, response.Output.Content)
            : new Message(MessageRole.Assistant, string.Empty);

        var clone = new Response(clonedMessage, clonedUsage)
        {
            FinishReason = response.FinishReason,
            ToolCalls = response.ToolCalls?.Select(tc => new ToolCall(tc.Name, tc.ArgumentsJson)).ToList(),
            ProviderRaw = response.ProviderRaw != null
            ? response.ProviderRaw.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            : null
        };

        return clone;
    }

    private sealed class CacheEntry
    {
        public Response Response { get; }
        public DateTimeOffset ExpiresAt { get; }

        public CacheEntry(Response response, DateTimeOffset expiresAt)
        {
            Response = response;
            ExpiresAt = expiresAt;
        }

        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    }
}
