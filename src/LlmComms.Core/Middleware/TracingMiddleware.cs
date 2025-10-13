using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using LlmComms.Abstractions.Contracts;
using LlmComms.Abstractions.Ports;

namespace LlmComms.Core.Middleware;

/// <summary>
/// Middleware that creates distributed tracing activities for observability.
/// </summary>
public sealed class TracingMiddleware : IMiddleware
{
    private static readonly ActivitySource _activitySource = new ActivitySource(
        "LlmComms",
        "1.0.0"
    );

    /// <summary>
    /// Invokes the middleware with tracing for non-streaming requests.
    /// </summary>
    public async Task<Response> InvokeAsync(
        LLMContext context,
        Func<LLMContext, Task<Response>> next)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        using var activity = _activitySource.StartActivity(
            $"llm.{context.Provider.Name}.{context.Model.ModelId}",
            ActivityKind.Client
        );

        if (activity != null)
        {
            // Set standard tags
            activity.SetTag("llm.provider", context.Provider.Name);
            activity.SetTag("llm.model", context.Model.ModelId);
            activity.SetTag("llm.request_id", context.CallContext.RequestId);
            activity.SetTag("llm.streaming", false);

            // Add RequestId as baggage for distributed tracing
            activity.SetBaggage("llm.request_id", context.CallContext.RequestId);

            // Add request parameters
            if (context.Request.Temperature.HasValue)
                activity.SetTag("llm.temperature", context.Request.Temperature.Value);

            if (context.Request.MaxOutputTokens.HasValue)
                activity.SetTag("llm.max_output_tokens", context.Request.MaxOutputTokens.Value);
        }

        try
        {
            var response = await next(context).ConfigureAwait(false);

            // Record success metrics
            if (activity != null)
            {
                activity.SetTag("llm.finish_reason", response.FinishReason?.ToString() ?? "unknown");
                activity.SetTag("llm.prompt_tokens", response.Usage.PromptTokens);
                activity.SetTag("llm.completion_tokens", response.Usage.CompletionTokens);
                activity.SetTag("llm.total_tokens", response.Usage.TotalTokens);
                activity.SetStatus(ActivityStatusCode.Ok);
            }

            return response;
        }
        catch (Exception ex)
        {
            // Record failure
            if (activity != null)
            {
                activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity.SetTag("llm.error.type", ex.GetType().Name);
                activity.SetTag("llm.error.message", ex.Message);
            }

            throw;
        }
    }

    /// <summary>
    /// Invokes the middleware with tracing for streaming requests.
    /// </summary>
    public async IAsyncEnumerable<StreamEvent> InvokeStreamAsync(
        LLMContext context,
        Func<LLMContext, IAsyncEnumerable<StreamEvent>> next)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        using var activity = _activitySource.StartActivity(
            $"llm.{context.Provider.Name}.{context.Model.ModelId}",
            ActivityKind.Client
        );

        if (activity != null)
        {
            activity.SetTag("llm.provider", context.Provider.Name);
            activity.SetTag("llm.model", context.Model.ModelId);
            activity.SetTag("llm.request_id", context.CallContext.RequestId);
            activity.SetTag("llm.streaming", true);
            activity.SetBaggage("llm.request_id", context.CallContext.RequestId);

            if (context.Request.Temperature.HasValue)
                activity.SetTag("llm.temperature", context.Request.Temperature.Value);

            if (context.Request.MaxOutputTokens.HasValue)
                activity.SetTag("llm.max_output_tokens", context.Request.MaxOutputTokens.Value);
        }

        var totalPromptTokens = 0;
        var totalCompletionTokens = 0;
        var hasError = false;

        await foreach (var streamEvent in next(context).ConfigureAwait(false))
        {
            // Accumulate token usage
            if (streamEvent.UsageDelta != null)
            {
                totalPromptTokens += streamEvent.UsageDelta.PromptTokens;
                totalCompletionTokens += streamEvent.UsageDelta.CompletionTokens;
            }

            // Check for errors
            if (streamEvent.Kind == StreamEventKind.Error)
            {
                hasError = true;
            }

            yield return streamEvent;
        }

        // Record final metrics
        if (activity != null)
        {
            if (hasError)
            {
                activity.SetStatus(ActivityStatusCode.Error);
            }
            else
            {
                activity.SetStatus(ActivityStatusCode.Ok);
                activity.SetTag("llm.prompt_tokens", totalPromptTokens);
                activity.SetTag("llm.completion_tokens", totalCompletionTokens);
                activity.SetTag("llm.total_tokens", totalPromptTokens + totalCompletionTokens);
            }
        }
    }
}