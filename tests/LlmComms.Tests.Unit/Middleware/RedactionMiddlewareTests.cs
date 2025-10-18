using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LlmComms.Abstractions.Contracts;
using LlmComms.Abstractions.Ports;
using LlmComms.Core.Middleware;
using NSubstitute;

namespace LlmComms.Tests.Unit.Middleware;

public sealed class RedactionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WithRedactionEnabled_PopulatesRedactedArtifacts()
    {
        var provider = Substitute.For<IProvider>();
        provider.Name.Returns("test-provider");

        var model = Substitute.For<IModel>();
        model.ModelId.Returns("test-model");

        var messages = new List<Message>
        {
            new(MessageRole.User, "Contact me at alice@example.com"),
            new(MessageRole.Assistant, "Sure thing.")
        };

        var request = new Request(messages);
        var callContext = new ProviderCallContext("req-123");
        var options = new ClientOptions { EnableRedaction = true };
        var context = new LLMContext(provider, model, request, callContext, options, CancellationToken.None);

        var middleware = new RedactionMiddleware();

        var response = new Response(
            new Message(MessageRole.Assistant, "Done."),
            new Usage(1, 1, 2));

        await middleware.InvokeAsync(context, _ => Task.FromResult(response));

        context.CallContext.Items.Should().ContainKey("llm.redacted.preview");
        context.CallContext.Items.Should().ContainKey("llm.redacted.messages");

        var preview = context.CallContext.Items["llm.redacted.preview"].Should().BeOfType<string>().Subject;
        preview.Should().Contain("***@***");

        var redactedMessages = context.CallContext.Items["llm.redacted.messages"]
            .Should().BeAssignableTo<IReadOnlyList<Message>>().Subject;
        redactedMessages.Last().Content.Should().NotContain("alice@example.com");

        request.Messages.First().Content.Should().Contain("alice@example.com");
    }

    [Fact]
    public async Task InvokeAsync_WithRedactionDisabled_DoesNotStoreRedactedMessages()
    {
        var provider = Substitute.For<IProvider>();
        provider.Name.Returns("test-provider");

        var model = Substitute.For<IModel>();
        model.ModelId.Returns("test-model");

        var messages = new List<Message>
        {
            new(MessageRole.User, "Ping me at bob@example.com")
        };

        var request = new Request(messages);
        var callContext = new ProviderCallContext("req-456");
        var options = new ClientOptions { EnableRedaction = false };
        var context = new LLMContext(provider, model, request, callContext, options, CancellationToken.None);

        var middleware = new RedactionMiddleware();

        var response = new Response(
            new Message(MessageRole.Assistant, "Done."),
            new Usage(1, 1, 2));

        await middleware.InvokeAsync(context, _ => Task.FromResult(response));

        context.CallContext.Items.Should().ContainKey("llm.redacted.preview");
        context.CallContext.Items.Should().NotContainKey("llm.redacted.messages");

        var preview = context.CallContext.Items["llm.redacted.preview"].Should().BeOfType<string>().Subject;
        preview.Should().Contain("bob@example.com");
    }
}
