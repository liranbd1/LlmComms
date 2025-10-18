using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LlmComms.Abstractions.Contracts;
using LlmComms.Abstractions.Ports;
using LlmComms.Providers.Ollama;

namespace LlmComms.Tests.Unit.Providers;

public sealed class OllamaProviderTests
{
    [Fact]
    public async Task SendAsync_BuildsExpectedPayloadAndMapsResponse()
    {
        var transport = new CapturingTransport(_ => Task.FromResult<object>(new
        {
            StatusCode = 200,
            Headers = new Dictionary<string, IEnumerable<string>>(),
            Body = "{\"model\":\"llama3.2\",\"message\":{\"role\":\"assistant\",\"content\":\"Hi!\",\"thinking\":\"Reasoning trace\",\"tool_calls\":[{\"name\":\"get_weather\",\"arguments\":{\"location\":\"Paris\"}}]},\"done\":true,\"done_reason\":\"stop\",\"prompt_eval_count\":12,\"eval_count\":8}"
        }));

        var provider = new OllamaProvider(new OllamaProviderOptions("http://localhost:11434"), transport);
        var model = provider.CreateModel("llama3.2");

        var request = new Request(new List<Message>
        {
            new(MessageRole.System, "You are helpful."),
            new(MessageRole.User, "Hello")
        })
        {
            Temperature = 0.7,
            TopP = 0.9,
            MaxOutputTokens = 64,
            ResponseFormat = ResponseFormat.JsonObject,
            Tools = new ToolCollection(new List<ToolDefinition>
            {
                new("get_weather", "Returns the weather", new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> { ["location"] = new Dictionary<string, object> { ["type"] = "string" } },
                    ["required"] = new[] { "location" }
                })
            })
        };

        var context = new ProviderCallContext("request-1");

        var response = await provider.SendAsync(model, request, context, CancellationToken.None);

        response.Output.Content.Should().Be("Hi!");
        response.FinishReason.Should().Be(FinishReason.Stop);
        response.Usage.PromptTokens.Should().Be(12);
        response.Usage.CompletionTokens.Should().Be(8);
        response.ToolCalls.Should().NotBeNull();
        response.ToolCalls!.Should().ContainSingle();
        response.ToolCalls![0].Name.Should().Be("get_weather");
        response.Reasoning.Should().NotBeNull();
        response.Reasoning!.Segments.Should().ContainSingle();
        response.Reasoning!.Segments[0].Text.Should().Be("Reasoning trace");

        var capturedBody = transport.GetCapturedBody();
        capturedBody.Should().Contain("\"format\"");
        var json = JsonDocument.Parse(capturedBody);
        var root = json.RootElement;
        root.GetProperty("model").GetString().Should().Be("llama3.2");
        root.GetProperty("stream").GetBoolean().Should().BeFalse();
        root.GetProperty("format").GetString().Should().Be("json");

        var options = root.GetProperty("options");
        options.GetProperty("temperature").GetDouble().Should().Be(0.7);
        options.GetProperty("top_p").GetDouble().Should().Be(0.9);
        options.GetProperty("num_predict").GetInt32().Should().Be(64);

        var tools = root.GetProperty("tools");
        tools.GetArrayLength().Should().Be(1);
        var function = tools[0].GetProperty("function");
        function.GetProperty("name").GetString().Should().Be("get_weather");
    }

    [Fact]
    public async Task SendAsync_AllowsHintOverrides()
    {
        var transport = new CapturingTransport(_ => Task.FromResult<object>(new
        {
            StatusCode = 200,
            Headers = new Dictionary<string, IEnumerable<string>>(),
            Body = "{\"model\":\"llama3.2\",\"message\":{\"role\":\"assistant\",\"content\":\"Hi\"},\"done\":true}"
        }));

        var provider = new OllamaProvider(new OllamaProviderOptions("http://localhost"), transport);
        var model = provider.CreateModel("llama3.2");

        var hints = new Dictionary<string, object>
        {
            ["ollama.options"] = new Dictionary<string, object>
            {
                ["temperature"] = 0.3,
                ["frequency_penalty"] = 1.25
            }
        };

        var request = new Request(new List<Message> { new(MessageRole.User, "Hello") })
        {
            Temperature = 0.7,
            ProviderHints = hints
        };

        await provider.SendAsync(model, request, new ProviderCallContext("req-2"), CancellationToken.None);

        var payload = JsonDocument.Parse(transport.GetCapturedBody()).RootElement;
        var options = payload.GetProperty("options");
        options.GetProperty("temperature").GetDouble().Should().Be(0.3);
        options.GetProperty("frequency_penalty").GetDouble().Should().BeApproximately(1.25, 1e-6);
    }

    [Fact]
    public async Task StreamAsync_YieldsDeltasToolCallsAndTerminal()
    {
        var streamBody = string.Join("\n", new[]
        {
            "{\"model\":\"llama3.2\",\"message\":{\"role\":\"assistant\",\"content\":\"Hello\",\"thinking\":\"step 1\"},\"done\":false}",
            "{\"model\":\"llama3.2\",\"message\":{\"role\":\"assistant\",\"content\":\" world\",\"thinking\":\"step 2\"},\"done\":false}",
            "{\"model\":\"llama3.2\",\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[{\"name\":\"get_weather\",\"arguments\":{\"location\":\"Rome\"}}]},\"done\":false}",
            "{\"model\":\"llama3.2\",\"done\":true,\"done_reason\":\"stop\",\"prompt_eval_count\":5,\"eval_count\":3}"
        }) + "\n";

        var transport = new CapturingTransport(_ => Task.FromResult<object>(new
        {
            StatusCode = 200,
            Headers = new Dictionary<string, IEnumerable<string>>(),
            Body = streamBody
        }));

        var provider = new OllamaProvider(new OllamaProviderOptions("http://localhost"), transport);
        var model = provider.CreateModel("llama3.2");
        var request = new Request(new List<Message> { new(MessageRole.User, "Hi") });

        var events = new List<StreamEvent>();
        await foreach (var evt in provider.StreamAsync(model, request, new ProviderCallContext("req-3"), CancellationToken.None))
        {
            events.Add(evt);
        }

        events.Should().HaveCount(6);
        events[0].Kind.Should().Be(StreamEventKind.Delta);
        events[0].TextDelta.Should().Be("Hello");
        events[1].Kind.Should().Be(StreamEventKind.Reasoning);
        events[1].ReasoningDelta!.Text.Should().Be("step 1");
        events[2].Kind.Should().Be(StreamEventKind.Delta);
        events[2].TextDelta.Should().Be(" world");
        events[3].Kind.Should().Be(StreamEventKind.Reasoning);
        events[3].ReasoningDelta!.Text.Should().Be("step 2");
        events[4].Kind.Should().Be(StreamEventKind.ToolCall);
        events[4].ToolCallDelta.Should().NotBeNull();
        events[4].ToolCallDelta!.Name.Should().Be("get_weather");
        events[5].Kind.Should().Be(StreamEventKind.Complete);
        events[5].IsTerminal.Should().BeTrue();
        events[5].UsageDelta.Should().NotBeNull();
        events[5].UsageDelta!.PromptTokens.Should().Be(5);
        events[5].UsageDelta!.CompletionTokens.Should().Be(3);
        events[5].ReasoningDelta.Should().NotBeNull();
        events[5].ReasoningDelta!.Text.Should().Be("step 1step 2");
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
