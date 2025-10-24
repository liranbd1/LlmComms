using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LlmComms.Abstractions.Contracts;
using LlmComms.Abstractions.Exceptions;
using LlmComms.Abstractions.Ports;
using LlmComms.Providers.OpenAI;

namespace LlmComms.Tests.Unit.Providers;

public sealed class OpenAIProviderTests
{
    [Fact]
    public async Task SendAsync_WithCustomTransport_ShapesPayloadAndMapsResponse()
    {
        var transport = new CapturingTransport(_ => Task.FromResult<object>(new
        {
            StatusCode = 200,
            Headers = new Dictionary<string, IEnumerable<string>>(),
            Body = "{\"id\":\"resp_123\",\"model\":\"gpt-4o-mini\",\"created\":1717080000,\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":[{\"text\":\"Hello from OpenAI\"}],\"tool_calls\":[{\"function\":{\"name\":\"lookup\",\"arguments\":{\"city\":\"Lisbon\"}}}]}}],\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":8,\"total_tokens\":21}}"
        }));

        var provider = new OpenAIProvider(new OpenAIProviderOptions
        {
            ApiKey = "test-key",
            Endpoint = new Uri("https://api.example.com/"),
        }, transport);

        var model = provider.CreateModel("gpt-4o-mini");

        var request = new Request(new List<Message>
        {
            new(MessageRole.System, "You are concise."),
            new(MessageRole.User, "Hello")
        })
        {
            Temperature = 0.5,
            TopP = 0.9,
            MaxOutputTokens = 256,
            ResponseFormat = ResponseFormat.JsonObject,
            Tools = new ToolCollection(new List<ToolDefinition>
            {
                new("lookup", "Lookup information", new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["city"] = new Dictionary<string, object> { ["type"] = "string" }
                    },
                    ["required"] = new[] { "city" }
                })
            })
        };

        var response = await provider.SendAsync(model, request, new ProviderCallContext("req-1"), CancellationToken.None);

        response.Output.Content.Should().Be("Hello from OpenAI");
        response.FinishReason.Should().Be(FinishReason.Stop);
        response.Usage.PromptTokens.Should().Be(12);
        response.Usage.CompletionTokens.Should().Be(8);
        response.ToolCalls.Should().ContainSingle();
        response.ToolCalls![0].Name.Should().Be("lookup");
        response.ToolCalls![0].ArgumentsJson.Should().Contain("Lisbon");
        response.ProviderRaw.Should().NotBeNull();
        response.ProviderRaw!["id"].Should().Be("resp_123");

        var captured = transport.GetCapturedBody();
        var json = JsonDocument.Parse(captured).RootElement;
        json.GetProperty("model").GetString().Should().Be("gpt-4o-mini");
        json.GetProperty("messages").EnumerateArray().Should().HaveCount(2);
        json.GetProperty("temperature").GetDouble().Should().BeApproximately(0.5, 1e-6);
        json.GetProperty("top_p").GetDouble().Should().BeApproximately(0.9, 1e-6);
        json.GetProperty("max_tokens").GetInt32().Should().Be(256);
        json.GetProperty("response_format").GetProperty("type").GetString().Should().Be("json_object");
        json.GetProperty("tools").EnumerateArray().Should().HaveCount(1);
    }

    [Fact]
    public async Task SendAsync_WithErrorStatus_ThrowsMappedException()
    {
        var transport = new CapturingTransport(_ => Task.FromResult<object>(new
        {
            StatusCode = 429,
            Headers = new Dictionary<string, IEnumerable<string>>(),
            Body = "{\"error\":{\"message\":\"Too many requests\",\"code\":\"rate_limit\"}}"
        }));

        var provider = new OpenAIProvider(new OpenAIProviderOptions { ApiKey = "key" }, transport);
        var model = provider.CreateModel("gpt-4o-mini");
        var request = new Request(new List<Message> { new(MessageRole.User, "Hi") });

        var act = () => provider.SendAsync(model, request, new ProviderCallContext("req-err"), CancellationToken.None);

        await act.Should().ThrowAsync<RateLimitedException>()
            .WithMessage("Too many requests");
    }

    [Fact]
    public void StreamAsync_WithCustomTransport_Throws()
    {
        var provider = new OpenAIProvider(new OpenAIProviderOptions { ApiKey = "key" }, new CapturingTransport(_ => Task.FromResult<object>(new
        {
            StatusCode = 200,
            Headers = new Dictionary<string, IEnumerable<string>>(),
            Body = "{}"
        })));

        var model = provider.CreateModel("gpt-4o-mini");
        var request = new Request(new List<Message> { new(MessageRole.User, "stream") });

        Action act = () => provider.StreamAsync(model, request, new ProviderCallContext("req-stream"), CancellationToken.None);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void CreateModel_ReturnsConfiguredModel()
    {
        var provider = new OpenAIProvider();
        var model = provider.CreateModel("gpt-4o");

        model.ModelId.Should().Be("gpt-4o");
        model.Format.Should().Be("chat");
        model.MaxInputTokens.Should().BeNull();
        model.MaxOutputTokens.Should().BeNull();
    }

    private sealed class CapturingTransport : ITransport
    {
        private readonly Func<object, Task<object>> _handler;

        public CapturingTransport(Func<object, Task<object>> handler)
        {
            _handler = handler;
        }

        public object? LastRequest { get; private set; }

        public Task<object> SendAsync(object request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return _handler(request);
        }

        public string GetCapturedBody()
        {
            LastRequest.Should().NotBeNull();
            var type = LastRequest!.GetType();
            var bodyProperty = type.GetProperty("Body");
            bodyProperty.Should().NotBeNull();
            return (string)bodyProperty!.GetValue(LastRequest!)!;
        }
    }
}
