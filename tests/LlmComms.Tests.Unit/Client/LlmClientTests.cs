using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LlmComms.Abstractions.Contracts;
using LlmComms.Abstractions.Ports;
using LlmComms.Core.Client;
using LlmComms.Core.Middleware;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LlmComms.Tests.Unit.Client;

public sealed class LlmClientTests
{
    [Fact]
    public async Task SendAsync_InvokesProviderThroughMiddlewareChain()
    {
        var provider = Substitute.For<IProvider>();
        provider.Name.Returns("provider-a");

        var model = Substitute.For<IModel>();
        model.ModelId.Returns("model-1");
        provider.CreateModel("model-1", Arg.Any<ProviderModelOptions?>()).Returns(model);

        var response = new Response(
            new Message(MessageRole.Assistant, "done"),
            new Usage(3, 2, 5))
        {
            FinishReason = FinishReason.Stop
        };

        Request? capturedRequest = null;
        provider.SendAsync(model, Arg.Any<Request>(), Arg.Any<ProviderCallContext>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedRequest = ci.ArgAt<Request>(1);
                return Task.FromResult(response);
            });

        var middlewareInvoked = false;
        var builder = new LlmClientBuilder()
            .UseProvider(provider)
            .UseModel("model-1")
            .UseLoggerFactory(NullLoggerFactory.Instance)
            .ConfigureMiddleware(pipeline =>
            {
                pipeline.Use(new TestMiddleware(() => middlewareInvoked = true));
            });

        var client = builder.Build();

        var request = new Request(new List<Message> { new(MessageRole.User, "hi") });

        var result = await client.SendAsync(request, CancellationToken.None);

        middlewareInvoked.Should().BeTrue();
        await provider.Received(1).SendAsync(model, Arg.Any<Request>(), Arg.Any<ProviderCallContext>(), Arg.Any<CancellationToken>());
        result.Should().BeSameAs(response);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.MaxOutputTokens.Should().Be(512);
    }

    [Fact]
    public async Task StreamAsync_WhenProviderDoesNotSupportStreaming_Throws()
    {
        var provider = Substitute.For<IProvider>();
        provider.Name.Returns("provider-b");

        var model = Substitute.For<IModel>();
        model.ModelId.Returns("model-2");
        provider.CreateModel("model-2", Arg.Any<ProviderModelOptions?>()).Returns(model);

        var builder = new LlmClientBuilder()
            .UseProvider(provider)
            .UseModel("model-2")
            .UseLoggerFactory(NullLoggerFactory.Instance);

        var client = builder.Build();
        var request = new Request(new List<Message> { new(MessageRole.User, "stream?") });

        provider.StreamAsync(model, Arg.Any<Request>(), Arg.Any<ProviderCallContext>(), Arg.Any<CancellationToken>())
            .Returns(_ => ThrowingStream());

        var act = () => client.StreamAsync(request, CancellationToken.None).GetAsyncEnumerator().MoveNextAsync().AsTask();

        await act.Should().ThrowAsync<NotSupportedException>();
        await provider.Received(1).StreamAsync(model, Arg.Any<Request>(), Arg.Any<ProviderCallContext>(), Arg.Any<CancellationToken>());
    }

    private sealed class TestMiddleware : IMiddleware
    {
        private readonly Action _onInvoke;

        public TestMiddleware(Action onInvoke)
        {
            _onInvoke = onInvoke;
        }

        public Task<Response> InvokeAsync(LLMContext context, Func<LLMContext, Task<Response>> next)
        {
            _onInvoke();
            return next(context);
        }

        public async IAsyncEnumerable<StreamEvent> InvokeStreamAsync(
            LLMContext context,
            Func<LLMContext, IAsyncEnumerable<StreamEvent>> next)
        {
            _onInvoke();
            await foreach (var item in next(context).ConfigureAwait(false))
            {
                yield return item;
            }
        }
    }

    private static async IAsyncEnumerable<StreamEvent> ThrowingStream()
    {
        await Task.Yield();
        throw new NotSupportedException("Streaming is not available for this model.");
        yield break;
    }
}
