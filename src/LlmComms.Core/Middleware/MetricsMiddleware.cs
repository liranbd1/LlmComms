using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;
using LlmComms.Abstractions.Contracts;
using LlmComms.Abstractions.Ports;

namespace LlmComms.Core.Middleware;

/// <summary>
/// Middleware that emits metrics for request lifecycle, duration, and token usage.
/// </summary>
public sealed class MetricsMiddleware : IMiddleware
{
    private static readonly Meter _meter = new("LlmComms", "1.0.0");
    private static readonly Counter<long> _requestCounter = _meter.CreateCounter<long>(
        "llm.requests.total",
        unit: "requests",
        description: "Total LLM requests processed by the client.");

    private static readonly Histogram<double> _requestDuration = _meter.CreateHistogram<double>(
        "llm.request.duration",
        unit: "ms",
        description: "Duration of LLM requests in milliseconds.");

    private static readonly Histogram<long> _promptTokens = _meter.CreateHistogram<long>(
        "llm.tokens.prompt",
        unit: "tokens",
        description: "Prompt tokens consumed per request.");

    private static readonly Histogram<long> _completionTokens = _meter.CreateHistogram<long>(
        "llm.tokens.completion",
        unit: "tokens",
        description: "Completion tokens generated per request.");

    private static readonly Histogram<long> _totalTokens = _meter.CreateHistogram<long>(
        "llm.tokens.total",
        unit: "tokens",
        description: "Total tokens processed per request.");

    /// <inheritdoc />
    public async Task<Response> InvokeAsync(
        LLMContext context,
        Func<LLMContext, Task<Response>> next)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        if (next == null)
            throw new ArgumentNullException(nameof(next));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await next(context).ConfigureAwait(false);
            stopwatch.Stop();

            RecordSuccess(
                context,
                streaming: false,
                outcome: "success",
                finishReason: response.FinishReason?.ToString(),
                durationMs: stopwatch.Elapsed.TotalMilliseconds,
                promptTokens: response.Usage?.PromptTokens ?? 0,
                completionTokens: response.Usage?.CompletionTokens ?? 0,
                totalTokens: response.Usage?.TotalTokens ?? ((response.Usage?.PromptTokens ?? 0) + (response.Usage?.CompletionTokens ?? 0)));

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            RecordFailure(
                context,
                streaming: false,
                outcome: "failure",
                errorType: ex.GetType().Name,
                durationMs: stopwatch.Elapsed.TotalMilliseconds);

            throw;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamEvent> InvokeStreamAsync(
        LLMContext context,
        Func<LLMContext, IAsyncEnumerable<StreamEvent>> next)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        if (next == null)
            throw new ArgumentNullException(nameof(next));

        var stopwatch = Stopwatch.StartNew();
        var promptTokens = 0;
        var completionTokens = 0;
        var hasErrorEvent = false;
        StreamEventKind? terminalKind = null;

        IAsyncEnumerable<StreamEvent> stream;
        try
        {
            stream = next(context);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            RecordFailure(
                context,
                streaming: true,
                outcome: "failure",
                errorType: ex.GetType().Name,
                durationMs: stopwatch.Elapsed.TotalMilliseconds);

            throw;
        }

        await using var enumerator = stream.GetAsyncEnumerator();
        while (true)
        {
            StreamEvent? streamEvent;
            try
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    break;

                streamEvent = enumerator.Current;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                RecordFailure(
                    context,
                    streaming: true,
                    outcome: "failure",
                    errorType: ex.GetType().Name,
                    durationMs: stopwatch.Elapsed.TotalMilliseconds,
                    promptTokens: promptTokens,
                    completionTokens: completionTokens);

                throw;
            }

            if (streamEvent == null)
                continue;

            if (streamEvent.UsageDelta != null)
            {
                promptTokens += streamEvent.UsageDelta.PromptTokens;
                completionTokens += streamEvent.UsageDelta.CompletionTokens;
            }

            if (streamEvent.Kind == StreamEventKind.Error)
                hasErrorEvent = true;

            if (streamEvent.IsTerminal)
                terminalKind = streamEvent.Kind;

            yield return streamEvent;
        }

        stopwatch.Stop();

        var outcome = hasErrorEvent ? "warning" : "success";
        var finishReason = terminalKind?.ToString();

        RecordSuccess(
            context,
            streaming: true,
            outcome: outcome,
            finishReason: finishReason,
            durationMs: stopwatch.Elapsed.TotalMilliseconds,
            promptTokens: promptTokens,
            completionTokens: completionTokens,
            totalTokens: promptTokens + completionTokens);
    }

    private static void RecordSuccess(
        LLMContext context,
        bool streaming,
        string outcome,
        string? finishReason,
        double durationMs,
        int promptTokens,
        int completionTokens,
        int totalTokens)
    {
        var tags = CreateTags(
            context,
            streaming,
            outcome,
            finishReason,
            errorType: null);

        _requestCounter.Add(1, tags);
        _requestDuration.Record(durationMs, tags);

        if (promptTokens > 0)
            _promptTokens.Record(promptTokens, tags);

        if (completionTokens > 0)
            _completionTokens.Record(completionTokens, tags);

        if (totalTokens > 0)
            _totalTokens.Record(totalTokens, tags);
    }

    private static void RecordFailure(
        LLMContext context,
        bool streaming,
        string outcome,
        string? errorType,
        double durationMs,
        int promptTokens = 0,
        int completionTokens = 0)
    {
        var tags = CreateTags(
            context,
            streaming,
            outcome,
            finishReason: null,
            errorType: errorType);

        _requestCounter.Add(1, tags);
        _requestDuration.Record(durationMs, tags);

        if (promptTokens > 0)
            _promptTokens.Record(promptTokens, tags);

        if (completionTokens > 0)
            _completionTokens.Record(completionTokens, tags);

        var totalTokens = promptTokens + completionTokens;
        if (totalTokens > 0)
            _totalTokens.Record(totalTokens, tags);
    }

    private static KeyValuePair<string, object?>[] CreateTags(
        LLMContext context,
        bool streaming,
        string outcome,
        string? finishReason,
        string? errorType)
    {
        return new[]
        {
            new KeyValuePair<string, object?>("llm.provider", context.Provider.Name),
            new KeyValuePair<string, object?>("llm.model", context.Model.ModelId),
            new KeyValuePair<string, object?>("llm.streaming", streaming),
            new KeyValuePair<string, object?>("llm.outcome", outcome),
            new KeyValuePair<string, object?>("llm.finish_reason", finishReason),
            new KeyValuePair<string, object?>("llm.error_type", errorType)
        };
    }
}
