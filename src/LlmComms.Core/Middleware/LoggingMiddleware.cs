using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using LlmComms.Abstractions.Contracts;
using LlmComms.Abstractions.Ports;
using LlmComms.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace LlmComms.Core.Middleware;

/// <summary>
/// Middleware that emits structured logs for request lifecycle events.
/// </summary>
public sealed class LoggingMiddleware : IMiddleware
{
    private static readonly EventId RequestStartEvent = new EventId(1000, "LlmRequestStart");
    private static readonly EventId RequestSucceededEvent = new EventId(1001, "LlmRequestSucceeded");
    private static readonly EventId RequestFailedEvent = new EventId(1002, "LlmRequestFailed");
    private static readonly EventId RequestCompletedWithWarningsEvent = new EventId(1003, "LlmRequestCompletedWithWarnings");

    private readonly ILogger<LoggingMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoggingMiddleware"/> class.
    /// </summary>
    /// <param name="logger">The logger to emit structured events.</param>
    public LoggingMiddleware(ILogger<LoggingMiddleware> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Response> InvokeAsync(
        LLMContext context,
        Func<LLMContext, Task<Response>> next)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        if (next == null)
            throw new ArgumentNullException(nameof(next));

        var requestId = context.CallContext.RequestId;
        var providerName = context.Provider.Name;
        var modelId = context.Model.ModelId;

        var stopwatch = Stopwatch.StartNew();

        if (_logger.IsEnabled(LogLevel.Information))
        {
            var messageCount = context.Request.Messages?.Count ?? 0;
            var requestHash = RequestHasher.ComputeHash(context.Request);
            var preview = TryGetRedactedPreview(context);

            _logger.LogInformation(
                RequestStartEvent,
                "LLM request starting. RequestId={RequestId} Provider={Provider} Model={Model} Streaming={Streaming} MessageCount={MessageCount} RequestHash={RequestHash}",
                requestId,
                providerName,
                modelId,
                false,
                messageCount,
                requestHash ?? string.Empty);

            if (!string.IsNullOrEmpty(preview) && _logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "LLM request preview. RequestId={RequestId} Preview=\"{Preview}\"",
                    requestId,
                    preview);
            }
        }

        try
        {
            var response = await next(context).ConfigureAwait(false);
            stopwatch.Stop();

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    RequestSucceededEvent,
                    "LLM request succeeded. RequestId={RequestId} Provider={Provider} Model={Model} Streaming={Streaming} DurationMs={DurationMs} FinishReason={FinishReason} PromptTokens={PromptTokens} CompletionTokens={CompletionTokens} TotalTokens={TotalTokens}",
                    requestId,
                    providerName,
                    modelId,
                    false,
                    stopwatch.Elapsed.TotalMilliseconds,
                    response.FinishReason?.ToString() ?? "unknown",
                    response.Usage?.PromptTokens ?? 0,
                    response.Usage?.CompletionTokens ?? 0,
                    response.Usage?.TotalTokens ?? 0);
            }

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(
                    RequestFailedEvent,
                    ex,
                    "LLM request failed. RequestId={RequestId} Provider={Provider} Model={Model} Streaming={Streaming} DurationMs={DurationMs}",
                    requestId,
                    providerName,
                    modelId,
                    false,
                    stopwatch.Elapsed.TotalMilliseconds);
            }

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

        var requestId = context.CallContext.RequestId;
        var providerName = context.Provider.Name;
        var modelId = context.Model.ModelId;

        var stopwatch = Stopwatch.StartNew();

        if (_logger.IsEnabled(LogLevel.Information))
        {
            var messageCount = context.Request.Messages?.Count ?? 0;
            var requestHash = RequestHasher.ComputeHash(context.Request);
            var preview = TryGetRedactedPreview(context);

            _logger.LogInformation(
                RequestStartEvent,
                "LLM request starting. RequestId={RequestId} Provider={Provider} Model={Model} Streaming={Streaming} MessageCount={MessageCount} RequestHash={RequestHash}",
                requestId,
                providerName,
                modelId,
                true,
                messageCount,
                requestHash ?? string.Empty);

            if (!string.IsNullOrEmpty(preview) && _logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "LLM request preview. RequestId={RequestId} Preview=\"{Preview}\"",
                    requestId,
                    preview);
            }
        }

        var promptTokens = 0;
        var completionTokens = 0;
        var emittedTerminal = false;
        var encounteredErrorEvent = false;

        IAsyncEnumerable<StreamEvent> stream;
        try
        {
            stream = next(context);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(
                    RequestFailedEvent,
                    ex,
                    "LLM streaming request failed before enumeration. RequestId={RequestId} Provider={Provider} Model={Model} Streaming={Streaming}",
                    requestId,
                    providerName,
                    modelId,
                    true);
            }

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

                if (_logger.IsEnabled(LogLevel.Error))
                {
                    _logger.LogError(
                        RequestFailedEvent,
                        ex,
                        "LLM streaming request failed during enumeration. RequestId={RequestId} Provider={Provider} Model={Model} Streaming={Streaming} DurationMs={DurationMs} PromptTokens={PromptTokens} CompletionTokens={CompletionTokens}",
                        requestId,
                        providerName,
                        modelId,
                        true,
                        stopwatch.Elapsed.TotalMilliseconds,
                        promptTokens,
                        completionTokens);
                }

                throw;
            }

            if (streamEvent == null)
                continue;

            if (streamEvent.UsageDelta != null)
            {
                promptTokens += streamEvent.UsageDelta.PromptTokens;
                completionTokens += streamEvent.UsageDelta.CompletionTokens;
            }

            if (streamEvent.IsTerminal)
                emittedTerminal = true;

            if (streamEvent.Kind == StreamEventKind.Error)
                encounteredErrorEvent = true;

            yield return streamEvent;
        }

        stopwatch.Stop();

        if (encounteredErrorEvent)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(
                    RequestCompletedWithWarningsEvent,
                    "LLM streaming request completed with errors. RequestId={RequestId} Provider={Provider} Model={Model} DurationMs={DurationMs} PromptTokens={PromptTokens} CompletionTokens={CompletionTokens} TerminalEventEmitted={TerminalEventEmitted}",
                    requestId,
                    providerName,
                    modelId,
                    stopwatch.Elapsed.TotalMilliseconds,
                    promptTokens,
                    completionTokens,
                    emittedTerminal);
            }
        }
        else if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                RequestSucceededEvent,
                "LLM streaming request succeeded. RequestId={RequestId} Provider={Provider} Model={Model} Streaming={Streaming} DurationMs={DurationMs} PromptTokens={PromptTokens} CompletionTokens={CompletionTokens} TotalTokens={TotalTokens} TerminalEventEmitted={TerminalEventEmitted}",
                requestId,
                providerName,
                modelId,
                true,
                stopwatch.Elapsed.TotalMilliseconds,
                promptTokens,
                completionTokens,
                promptTokens + completionTokens,
                emittedTerminal);
        }
    }

    private static string? TryGetRedactedPreview(LLMContext context)
    {
        if (context.CallContext.Items.TryGetValue(RedactionMiddleware.RedactedPreviewKey, out var value) &&
            value is string preview)
        {
            return preview;
        }

        return null;
    }
}
