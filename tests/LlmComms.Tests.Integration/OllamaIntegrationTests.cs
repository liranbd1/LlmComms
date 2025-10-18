using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LlmComms.Abstractions.Contracts;
using LlmComms.Core.Client;
using LlmComms.Providers.Ollama;

namespace LlmComms.Tests.Integration;

public sealed class OllamaIntegrationTests
{
    [Fact]
    public async Task SendAsync_WithQwenModel_ReturnsResponse()
    {
        var provider = new OllamaProvider();

        var client = new LlmClientBuilder()
            .UseProvider(provider)
            .UseModel("qwen3:4b")
            .Build();

        var request = new Request(new List<Message>
        {
            new(MessageRole.System, "You are a friendly assistant."),
            new(MessageRole.User, "Say hello in one short sentence.")
        });

        var response = await client.SendAsync(request, CancellationToken.None);

        response.Output.Content.Should().NotBeNullOrWhiteSpace();
    }
}
