using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using FluentAssertions;
using LlmComms.Abstractions.Contracts;
using LlmComms.Abstractions.Exceptions;
using LlmComms.Abstractions.Ports;
using LlmComms.Providers.Azure;
using LlmComms.Core.Transport;

namespace LlmComms.Tests.Unit.Providers;

public sealed class AzureOpenAIProviderTests
{
    [Fact]
    public async Task SendAsync_WithCustomTransport_ShapesPayloadAndMapsResponse()
    {
        var transport = new CapturingTransport(_ => Task.FromResult<object>(new
        {
            StatusCode = 200,
            Body = "{\"id\":\"resp_123\",\"model\":\"gpt-4o-mini\",\"created\":1717080000,\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":[{\"text\":\"Hello from Azure\"}],\"tool_calls\":[{\"function\":{\"name\":\"lookup\",\"arguments\":{\"city\":\"Lisbon\"}}}]}}],\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":8,\"total_tokens\":21}}"
        }));

        var provider = new AzureOpenAIProvider(new AzureOpenAIProviderOptions
        {
            ResourceName = "my-resource",
            Credential = new StubTokenCredential("test-token"),
            DefaultDeploymentId = "gpt-4o-mini"
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

        var context = new ProviderCallContext("req-azure-1");
        var response = await provider.SendAsync(model, request, context, CancellationToken.None);

        response.Output.Content.Should().Be("Hello from Azure");
        response.FinishReason.Should().Be(FinishReason.Stop);
        response.Usage.PromptTokens.Should().Be(12);
        response.Usage.CompletionTokens.Should().Be(8);
        response.Usage.TotalTokens.Should().Be(21);
        response.ToolCalls.Should().ContainSingle();
        response.ToolCalls![0].Name.Should().Be("lookup");
        response.ToolCalls![0].ArgumentsJson.Should().Contain("Lisbon");
        response.ProviderRaw.Should().NotBeNull();
        response.ProviderRaw!["id"].Should().Be("resp_123");

        var captured = transport.LastRequest.Should().BeOfType<HttpTransportRequest>().Subject;
        captured.Url.Should().Be("https://my-resource.openai.azure.com/openai/deployments/gpt-4o-mini/chat/completions?api-version=2024-05-01-preview");
        captured.Headers.Should().ContainKey("Authorization").WhoseValue.Should().Be("Bearer test-token");
        captured.Headers.Should().ContainKey("Content-Type").WhoseValue.Should().Be("application/json");
        captured.Headers.Should().ContainKey("x-ms-client-request-id").WhoseValue.Should().Be("req-azure-1");

        var json = JsonDocument.Parse(captured.Body).RootElement;
        json.GetProperty("messages").EnumerateArray().Should().HaveCount(2);
        json.GetProperty("temperature").GetDouble().Should().BeApproximately(0.5, 1e-6);
        json.GetProperty("tools").EnumerateArray().Should().HaveCount(1);
    }

    [Fact]
    public async Task SendAsync_WithErrorStatus_ThrowsMappedException()
    {
        var transport = new CapturingTransport(_ => Task.FromResult<object>(new
        {
            StatusCode = 429,
            Body = "{\"error\":{\"message\":\"Too many requests\",\"code\":\"rate_limit\"}}"
        }));

        var provider = new AzureOpenAIProvider(new AzureOpenAIProviderOptions
        {
            ResourceName = "my-resource",
            Credential = new StubTokenCredential("token"),
            DefaultDeploymentId = "gpt-4o-mini"
        }, transport);

        var model = provider.CreateModel("gpt-4o-mini");
        var request = new Request(new List<Message> { new(MessageRole.User, "Hi") });

        var act = () => provider.SendAsync(model, request, new ProviderCallContext("req-err"), CancellationToken.None);

        await act.Should().ThrowAsync<RateLimitedException>()
            .WithMessage("Too many requests");
    }

    [Fact]
    public void StreamAsync_WithCustomTransport_Throws()
    {
        var transport = new CapturingTransport(_ => Task.FromResult<object>(new
        {
            StatusCode = 200,
            Body = "{}"
        }));

        var provider = new AzureOpenAIProvider(new AzureOpenAIProviderOptions
        {
            ResourceName = "my-resource",
            Credential = new StubTokenCredential("token"),
            DefaultDeploymentId = "gpt-4o-mini"
        }, transport);

        var model = provider.CreateModel("gpt-4o-mini");
        var request = new Request(new List<Message> { new(MessageRole.User, "stream") });

        Action act = () => provider.StreamAsync(model, request, new ProviderCallContext("req-stream"), CancellationToken.None);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void CreateModel_UsesDefaultDeploymentWhenModelIdMissing()
    {
        var provider = new AzureOpenAIProvider(new AzureOpenAIProviderOptions
        {
            DefaultDeploymentId = "default-deployment"
        });

        var model = provider.CreateModel(string.Empty);
        model.ModelId.Should().Be("default-deployment");
    }

    private sealed class CapturingTransport : ITransport
    {
        private readonly Func<object, Task<object>> _handler;

        public CapturingTransport(Func<object, Task<object>> handler)
        {
            _handler = handler;
        }

        public HttpTransportRequest? LastRequest { get; private set; }

        public Task<object> SendAsync(object request, CancellationToken cancellationToken)
        {
            LastRequest = request as HttpTransportRequest;
            return _handler(request);
        }
    }

    private sealed class StubTokenCredential : TokenCredential
    {
        private readonly string _token;

        public StubTokenCredential(string token)
        {
            _token = token;
        }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(_token, DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(new AccessToken(_token, DateTimeOffset.MaxValue));
    }
}
