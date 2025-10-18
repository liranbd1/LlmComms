using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LlmComms.Abstractions.Contracts;
using LlmComms.Abstractions.Ports;
using LlmComms.Core.Middleware;
using NSubstitute;

namespace LlmComms.Tests.Unit.Middleware;

public sealed class MetricsMiddlewareTests : IDisposable
{
    private readonly MeterListener _listener;
    private readonly List<(string Instrument, double Value, KeyValuePair<string, object?>[] Tags)> _doubleMeasurements = new();
    private readonly List<(string Instrument, long Value, KeyValuePair<string, object?>[] Tags)> _longMeasurements = new();

    public MetricsMiddlewareTests()
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "LlmComms")
                    listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            _doubleMeasurements.Add((instrument.Name, measurement, tags.ToArray()));
        });

        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            _longMeasurements.Add((instrument.Name, measurement, tags.ToArray()));
        });

        _listener.Start();
    }

    [Fact]
    public async Task InvokeAsync_RecordsSuccessMetrics()
    {
        var provider = Substitute.For<IProvider>();
        provider.Name.Returns("metrics-provider");

        var model = Substitute.For<IModel>();
        model.ModelId.Returns("metrics-model");

        var request = new Request(new List<Message> { new(MessageRole.User, "hello") });
        var options = new ClientOptions();
        var context = new LLMContext(provider, model, request, new ProviderCallContext("req-789"), options, CancellationToken.None);

        var middleware = new MetricsMiddleware();
        var response = new Response(new Message(MessageRole.Assistant, "world"), new Usage(10, 5, 15))
        {
            FinishReason = FinishReason.Stop
        };

        await middleware.InvokeAsync(context, _ => Task.FromResult(response));

        var requestCounts = _longMeasurements.Where(m => m.Instrument == "llm.requests.total").ToList();
        requestCounts.Should().NotBeEmpty();
        requestCounts.Should().Contain(m => HasTag(m.Tags, "llm.provider", "metrics-provider"));

        var promptTokens = _longMeasurements
            .Where(m => m.Instrument == "llm.tokens.prompt" && HasTag(m.Tags, "llm.provider", "metrics-provider"))
            .ToList();
        promptTokens.Should().ContainSingle().Which.Value.Should().Be(10);

        var completionTokens = _longMeasurements
            .Where(m => m.Instrument == "llm.tokens.completion" && HasTag(m.Tags, "llm.provider", "metrics-provider"))
            .ToList();
        completionTokens.Should().ContainSingle().Which.Value.Should().Be(5);

        var totalTokens = _longMeasurements
            .Where(m => m.Instrument == "llm.tokens.total" && HasTag(m.Tags, "llm.provider", "metrics-provider"))
            .ToList();
        totalTokens.Should().ContainSingle().Which.Value.Should().Be(15);

        var durations = _doubleMeasurements
            .Where(m => m.Instrument == "llm.request.duration" && HasTag(m.Tags, "llm.provider", "metrics-provider"))
            .ToList();
        durations.Should().NotBeEmpty();
        durations.Should().Contain(m => HasTag(m.Tags, "llm.finish_reason", FinishReason.Stop.ToString()));
    }

    private static bool HasTag(IEnumerable<KeyValuePair<string, object?>> tags, string key, object? expected)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == key && Equals(tag.Value, expected))
                return true;
        }

        return false;
    }

    public void Dispose()
    {
        _listener.Dispose();
    }
}
