using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LlmComms.Abstractions.Contracts;
using LlmComms.Abstractions.Ports;

namespace LlmComms.Core.Middleware;

/// <summary>
/// Terminal middleware that invokes the provider.
/// This should always be the last middleware in the chain.
/// </summary>
public sealed class TerminalMiddleware : IMiddleware
{
    /// <summary>
    /// Invokes the provider for a non-streaming request.
    /// </summary>
    /// <param name="context">The execution context.</param>
    /// <param name="next">Not used (this is terminal).</param>
    /// <returns>The LLM response.</returns>
    public Task<Response> InvokeAsync(
    LLMContext context,
    Func<LLMContext, Task<Response>> next)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        // Use CT from context
        return context.Provider.SendAsync(
            context.Model,
            context.Request,
            context.CallContext,
            context.CancellationToken // ✅ Now available
        );
    }

    /// <summary>
    /// Invokes the provider for a streaming request.
    /// </summary>
    /// <param name="context">The execution context.</param>
    /// <param name="next">Not used (this is terminal).</param>
    /// <returns>An async stream of response events.</returns>
    public IAsyncEnumerable<StreamEvent> InvokeStreamAsync(
    LLMContext context,
    Func<LLMContext, IAsyncEnumerable<StreamEvent>> next)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        // Use CT from context
        return context.Provider.StreamAsync(
            context.Model,
            context.Request,
            context.CallContext,
            context.CancellationToken // ✅ Now available
        );
    }
}