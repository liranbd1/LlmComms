using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LlmComms.Abstractions.Contracts;
using LlmComms.Abstractions.Ports;
using LlmComms.Core.Middleware;
using NSubstitute;

namespace LlmComms.Tests.Unit.Middleware;

public sealed class CacheMiddlewareTests
{
    private readonly Response _sampleResponse = new(
        new Message(MessageRole.Assistant, "cached"),
        new Usage(1, 1, 2));

    [Fact]
    public async Task InvokeAsync_WhenCacheHit_SkipsNext()
    {
        var cache = Substitute.For<ILLMCache>();
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Response?>(_sampleResponse));

        var context = CreateContext();
        var middleware = new CacheMiddleware(cache);

        var nextCalled = false;
        var result = await middleware.InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return Task.FromResult(new Response(
                new Message(MessageRole.Assistant, "fresh"),
                new Usage(1, 1, 2)));
        });

        nextCalled.Should().BeFalse();
        result.Should().BeSameAs(_sampleResponse);
        context.CallContext.Items.Should().ContainKey(CacheMiddleware.CacheHitKey);
    }

    [Fact]
    public async Task InvokeAsync_WhenCacheMiss_StoresResponse()
    {
        var cache = Substitute.For<ILLMCache>();
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Response?>(null));

        var context = CreateContext();
        var middleware = new CacheMiddleware(cache);

        await middleware.InvokeAsync(context, _ => Task.FromResult(_sampleResponse));

        await cache.Received(1).SetAsync(
            Arg.Any<string>(),
            _sampleResponse,
            Arg.Is<TimeSpan>(ttl => ttl > TimeSpan.Zero),
            Arg.Any<CancellationToken>());

        context.CallContext.Items.Should().ContainKey(CacheMiddleware.CacheStoredKey);
    }

    [Fact]
    public async Task InvokeAsync_NoCacheHint_SkipsCacheCompletely()
    {
        var context = CreateContext(providerHints: new Dictionary<string, object>
        {
            ["no_cache"] = true
        });

        var cache = Substitute.For<ILLMCache>();
        var middleware = new CacheMiddleware(cache);

        await middleware.InvokeAsync(context, _ => Task.FromResult(_sampleResponse));

        await cache.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await cache.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<Response>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        context.CallContext.Items.Should().NotContainKey(CacheMiddleware.CacheHitKey);
    }

    [Fact]
    public async Task InvokeAsync_UsesTtlFromHintsWhenProvided()
    {
        var cache = Substitute.For<ILLMCache>();
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Response?>(null));

        var context = CreateContext(providerHints: new Dictionary<string, object>
        {
            ["cache_ttl_seconds"] = 42
        });

        var middleware = new CacheMiddleware(cache);

        await middleware.InvokeAsync(context, _ => Task.FromResult(_sampleResponse));

        await cache.Received(1).SetAsync(
            Arg.Any<string>(),
            _sampleResponse,
            Arg.Is<TimeSpan>(ttl => Math.Abs(ttl.TotalSeconds - 42) < 0.1),
            Arg.Any<CancellationToken>());
    }

    private static LLMContext CreateContext(IReadOnlyDictionary<string, object>? providerHints = null)
    {
        var provider = Substitute.For<IProvider>();
        provider.Name.Returns("cache-provider");
        provider.Capabilities.Returns(new ProviderCapabilities());

        var model = Substitute.For<IModel>();
        model.ModelId.Returns("cache-model");

        var request = new Request(new List<Message> { new(MessageRole.User, "hello") })
        {
            ProviderHints = providerHints
        };

        var options = new ClientOptions();
        var callContext = new ProviderCallContext("cache-request");

        return new LLMContext(
            provider,
            model,
            request,
            callContext,
            options,
            CancellationToken.None);
    }
}
