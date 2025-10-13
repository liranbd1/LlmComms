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

public sealed class LoggingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_LogsStartAndSuccess()
    {
        var logger = new TestLogger<LoggingMiddleware>();
        var middleware = new LoggingMiddleware(logger);

        var provider = Substitute.For<IProvider>();
        provider.Name.Returns("provider-x");
        provider.Capabilities.Returns(new ProviderCapabilities());

        var model = Substitute.For<IModel>();
        model.ModelId.Returns("model-y");

        var request = new Request(new List<Message> { new(MessageRole.User, "hello world") })
        {
            Temperature = 0.2
        };

        var context = new LLMContext(
            provider,
            model,
            request,
            new ProviderCallContext("req-001"),
            new ClientOptions(),
            CancellationToken.None);

        var response = new Response(
            new Message(MessageRole.Assistant, "hi"),
            new Usage(5, 3, 8))
        {
            FinishReason = FinishReason.Stop
        };

        await middleware.InvokeAsync(context, _ => Task.FromResult(response));

        logger.Entries.Should().HaveCount(2);
        logger.Entries[0].EventId.Id.Should().Be(1000);
        logger.Entries[1].EventId.Id.Should().Be(1001);

        logger.Entries[0].Message.Should().Contain("LLM request starting");
        logger.Entries[1].Message.Should().Contain("LLM request succeeded");
        logger.Entries[1].Message.Should().Contain("PromptTokens=5");
    }

    [Fact]
    public async Task InvokeAsync_WhenNextThrows_LogsFailureAndRethrows()
    {
        var logger = new TestLogger<LoggingMiddleware>();
        var middleware = new LoggingMiddleware(logger);

        var provider = Substitute.For<IProvider>();
        provider.Name.Returns("provider-x");
        provider.Capabilities.Returns(new ProviderCapabilities());

        var model = Substitute.For<IModel>();
        model.ModelId.Returns("model-y");

        var request = new Request(new List<Message> { new(MessageRole.User, "hello world") });

        var context = new LLMContext(
            provider,
            model,
            request,
            new ProviderCallContext("req-002"),
            new ClientOptions(),
            CancellationToken.None);

        var exception = new InvalidOperationException("boom");

        var act = () => middleware.InvokeAsync(context, _ => throw exception);

        var caught = await act.Should().ThrowAsync<InvalidOperationException>();
        caught.Which.Should().BeSameAs(exception);

        logger.Entries.Should().HaveCount(2);
        logger.Entries[0].EventId.Id.Should().Be(1000);
        logger.Entries[1].EventId.Id.Should().Be(1002);
        logger.Entries[1].Message.Should().Contain("LLM request failed");
    }

    private sealed class TestLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(
        Microsoft.Extensions.Logging.LogLevel Level,
        Microsoft.Extensions.Logging.EventId EventId,
        string Message);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }
}
