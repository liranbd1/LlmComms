using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LlmComms.Abstractions.Contracts;
using LlmComms.Core.Cache;

namespace LlmComms.Tests.Unit.Cache;

public sealed class InMemoryCacheTests
{
    private readonly InMemoryCache _cache = new();

    [Fact]
    public async Task GetAsync_ReturnsStoredResponse()
    {
        var response = CreateResponse("hello");
        await _cache.SetAsync("key", response, TimeSpan.FromMinutes(1), CancellationToken.None);

        var cached = await _cache.GetAsync("key", CancellationToken.None);

        cached.Should().NotBeNull();
        cached!.Output.Content.Should().Be("hello");
        cached.Should().NotBeSameAs(response);
    }

    [Fact]
    public async Task GetAsync_ExpiredEntryReturnsNull()
    {
        var response = CreateResponse("expired");
        await _cache.SetAsync("expire", response, TimeSpan.FromMilliseconds(10), CancellationToken.None);

        await Task.Delay(50);

        var cached = await _cache.GetAsync("expire", CancellationToken.None);
        cached.Should().BeNull();
    }

    private static Response CreateResponse(string content)
    {
        return new Response(
            new Message(MessageRole.Assistant, content),
            new Usage(1, 1, 2));
    }
}
