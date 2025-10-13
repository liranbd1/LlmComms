using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using LlmComms.Abstractions.Contracts;
using LlmComms.Abstractions.Ports;
using LlmComms.Core.Utilities;

namespace LlmComms.Core.Middleware;

/// <summary>
/// Middleware that provides response caching for non-streamed requests.
/// </summary>
public sealed class CacheMiddleware : IMiddleware
{
    public const string CacheHitKey = "llm.cache.hit";
    public const string CacheStoredKey = "llm.cache.stored";

    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private readonly ILLMCache _cache;
    private readonly TimeSpan _defaultTtl;

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheMiddleware"/> class.
    /// </summary>
    /// <param name="cache">The cache implementation.</param>
    /// <param name="defaultTtl">Optional default time-to-live for cached responses.</param>
    public CacheMiddleware(ILLMCache cache, TimeSpan? defaultTtl = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _defaultTtl = defaultTtl.GetValueOrDefault(DefaultTtl);
    }

    /// <inheritdoc />
    public async Task<Response> InvokeAsync(
        LLMContext context,
        Func<LLMContext, Task<Response>> next)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        if (next == null)
            throw new ArgumentNullException(nameof(next));

        if (ShouldBypassCache(context))
            return await next(context).ConfigureAwait(false);

        var cacheKey = BuildCacheKey(context);
        var cached = await _cache.GetAsync(cacheKey, context.CancellationToken).ConfigureAwait(false);
        if (cached != null)
        {
            context.CallContext.Items[CacheHitKey] = true;
            return cached;
        }

        var response = await next(context).ConfigureAwait(false);

        if (CanCacheResponse(response))
        {
            var ttl = ResolveTtl(context);
            if (ttl > TimeSpan.Zero)
            {
                await _cache.SetAsync(cacheKey, response, ttl, context.CancellationToken).ConfigureAwait(false);
                context.CallContext.Items[CacheStoredKey] = true;
            }
        }

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

        await foreach (var streamEvent in next(context).ConfigureAwait(false))
        {
            yield return streamEvent;
        }
    }

    private static bool ShouldBypassCache(LLMContext context)
    {
        if (context.Request.ProviderHints != null &&
            TryGetBoolean(context.Request.ProviderHints, "no_cache", out var noCache) &&
            noCache)
        {
            return true;
        }

        return false;
    }

    private static bool TryGetBoolean(IReadOnlyDictionary<string, object> hints, string key, out bool value)
    {
        value = false;
        if (!hints.TryGetValue(key, out var raw) || raw == null)
            return false;

        switch (raw)
        {
            case bool b:
                value = b;
                return true;
            case string s when bool.TryParse(s, out var parsed):
                value = parsed;
                return true;
            case int i:
                value = i != 0;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetDouble(IReadOnlyDictionary<string, object> hints, string key, out double value)
    {
        value = 0;
        if (!hints.TryGetValue(key, out var raw) || raw == null)
            return false;

        switch (raw)
        {
            case double d:
                value = d;
                return true;
            case float f:
                value = f;
                return true;
            case int i:
                value = i;
                return true;
            case long l:
                value = l;
                return true;
            case string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                value = parsed;
                return true;
            default:
                return false;
        }
    }

    private static bool CanCacheResponse(Response response)
    {
        if (response == null)
            return false;

        if (response.ToolCalls is { Count: > 0 })
            return false;

        return true;
    }

    private static string BuildCacheKey(LLMContext context)
    {
        var requestHash = RequestHasher.ComputeHash(context.Request);
        return $"{context.Provider.Name}:{context.Model.ModelId}:{requestHash}";
    }

    private TimeSpan ResolveTtl(LLMContext context)
    {
        if (context.Request.ProviderHints != null &&
            (TryGetDouble(context.Request.ProviderHints, "cache_ttl_seconds", out var seconds) ||
             TryGetDouble(context.Request.ProviderHints, "cache_ttl", out seconds)))
        {
            if (seconds > 0)
                return TimeSpan.FromSeconds(seconds);
        }

        return _defaultTtl;
    }
}
