using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LlmComms.Abstractions.Contracts;
using LlmComms.Abstractions.Exceptions;
using LlmComms.Abstractions.Ports;
using LlmComms.Core.Middleware;
using NSubstitute;

namespace LlmComms.Tests.Unit.Middleware;

public sealed class ValidatorMiddlewareTests
{
    private readonly ValidatorMiddleware _middleware = new();

    [Fact]
    public async Task InvokeAsync_ThrowsWhenProviderDoesNotSupportJson()
    {
        var context = CreateContext(
            providerCapabilities: new ProviderCapabilities { SupportsJsonMode = false },
            request: new Request(new List<Message> { new(MessageRole.User, "hello") })
            {
                ResponseFormat = ResponseFormat.JsonObject
            });

        var act = () => _middleware.InvokeAsync(context, _ => Task.FromResult(CreateValidResponse()));

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*does not support JSON*");
    }

    [Fact]
    public async Task InvokeAsync_InvalidJsonThrowsInStrictMode()
    {
        var context = CreateContext(
            providerCapabilities: new ProviderCapabilities { SupportsJsonMode = true },
            request: new Request(new List<Message> { new(MessageRole.User, "hello") })
            {
                ResponseFormat = ResponseFormat.JsonObject
            },
            options: new ClientOptions { ThrowOnInvalidJson = true });

        var response = CreateResponse("{not json");

        var act = () => _middleware.InvokeAsync(context, _ => Task.FromResult(response));

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*valid JSON*");
    }

    [Fact]
    public async Task InvokeAsync_InvalidJsonMarksProviderRawInLenientMode()
    {
        var context = CreateContext(
            providerCapabilities: new ProviderCapabilities { SupportsJsonMode = true },
            request: new Request(new List<Message> { new(MessageRole.User, "hello") })
            {
                ResponseFormat = ResponseFormat.JsonObject
            },
            options: new ClientOptions { ThrowOnInvalidJson = false });

        var response = CreateResponse("{not json");

        var result = await _middleware.InvokeAsync(context, _ => Task.FromResult(response));

        result.ProviderRaw.Should().NotBeNull();
        result.ProviderRaw!.Should().ContainKey("json_invalid").WhoseValue.Should().Be(true);
    }

    [Fact]
    public async Task InvokeAsync_ToolCallNameMismatchThrows()
    {
        var tools = new ToolCollection(new[]
        {
            new ToolDefinition("weather", "Get weather", new Dictionary<string, object>())
        });

        var context = CreateContext(
            providerCapabilities: new ProviderCapabilities { SupportsTools = true },
            request: new Request(new List<Message> { new(MessageRole.User, "hello") })
            {
                Tools = tools
            });

        var response = CreateValidResponse();
        response.ToolCalls = new[] { new ToolCall("calendar", "{\"day\":\"monday\"}") };

        var act = () => _middleware.InvokeAsync(context, _ => Task.FromResult(response));

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*not part of the declared tool collection*");
    }

    [Fact]
    public async Task InvokeAsync_ToolCallMissingRequiredPropertyThrows()
    {
        var tools = new ToolCollection(new[]
        {
            new ToolDefinition(
                "weather",
                "Get weather",
                new Dictionary<string, object>
                {
                    ["required"] = new[] { "city" }
                })
        });

        var context = CreateContext(
            providerCapabilities: new ProviderCapabilities { SupportsTools = true },
            request: new Request(new List<Message> { new(MessageRole.User, "hello") })
            {
                Tools = tools
            });

        var response = CreateValidResponse();
        response.ToolCalls = new[] { new ToolCall("weather", "{\"country\":\"us\"}") };

        var act = () => _middleware.InvokeAsync(context, _ => Task.FromResult(response));

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*missing required argument*");
    }

    [Fact]
    public async Task InvokeStreamAsync_InvalidJsonThrowsInStrictMode()
    {
        var context = CreateContext(
            providerCapabilities: new ProviderCapabilities { SupportsStreaming = true, SupportsJsonMode = true },
            request: new Request(new List<Message> { new(MessageRole.User, "hello") })
            {
                ResponseFormat = ResponseFormat.JsonObject
            });

        static async IAsyncEnumerable<StreamEvent> Stream()
        {
            yield return new StreamEvent(StreamEventKind.Delta) { TextDelta = "{invalid" };
            yield return new StreamEvent(StreamEventKind.Complete) { IsTerminal = true };
            await Task.CompletedTask;
        }

        var act = async () =>
        {
            await foreach (var _ in _middleware.InvokeStreamAsync(context, _ => Stream()))
            {
            }
        };

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*valid JSON*");
    }

    [Fact]
    public async Task InvokeStreamAsync_LenientModeMarksContext()
    {
        var options = new ClientOptions
        {
            ThrowOnInvalidJson = false
        };

        var context = CreateContext(
            providerCapabilities: new ProviderCapabilities { SupportsStreaming = true, SupportsJsonMode = true },
            request: new Request(new List<Message> { new(MessageRole.User, "hello") })
            {
                ResponseFormat = ResponseFormat.JsonObject
            },
            options: options);

        static async IAsyncEnumerable<StreamEvent> Stream()
        {
            yield return new StreamEvent(StreamEventKind.Delta) { TextDelta = "{invalid" };
            yield return new StreamEvent(StreamEventKind.Complete) { IsTerminal = true };
            await Task.CompletedTask;
        }

        await foreach (var _ in _middleware.InvokeStreamAsync(context, _ => Stream()))
        {
        }

        context.CallContext.Items.Should().ContainKey("llm.validation.json_invalid");
    }

    private static Response CreateValidResponse()
    {
        return new Response(
            new Message(MessageRole.Assistant, "{\"status\":\"ok\"}"),
            new Usage(1, 1, 2))
        {
            FinishReason = FinishReason.Stop
        };
    }

    private static Response CreateResponse(string content)
    {
        return new Response(
            new Message(MessageRole.Assistant, content),
            new Usage(1, 1, 2));
    }

    private static LLMContext CreateContext(
        ProviderCapabilities providerCapabilities,
        Request request,
        ClientOptions? options = null)
    {
        var provider = Substitute.For<IProvider>();
        provider.Name.Returns("validator-provider");
        provider.Capabilities.Returns(providerCapabilities);

        var model = Substitute.For<IModel>();
        model.ModelId.Returns("validator-model");

        options ??= new ClientOptions();

        return new LLMContext(
            provider,
            model,
            request,
            new ProviderCallContext("req-validator"),
            options,
            CancellationToken.None);
    }
}
