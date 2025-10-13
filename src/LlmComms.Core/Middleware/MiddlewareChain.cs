using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LlmComms.Abstractions.Contracts;
using LlmComms.Abstractions.Ports;

namespace LlmComms.Core.Middleware;

/// <summary>
/// Orchestrates the middleware pipeline execution.
/// </summary>
public sealed class MiddlewareChain
{
    private readonly IReadOnlyList<IMiddleware> _middlewares;

    /// <summary>
    /// Initializes a new instance of the <see cref="MiddlewareChain"/> class.
    /// </summary>
    /// <param name="middlewares">The middleware components in execution order (terminal should be last).</param>
    public MiddlewareChain(IEnumerable<IMiddleware> middlewares)
    {
        if (middlewares == null)
            throw new ArgumentNullException(nameof(middlewares));

        _middlewares = middlewares.ToList();

        if (_middlewares.Count == 0)
            throw new ArgumentException("At least one middleware (terminal) must be provided.", nameof(middlewares));
    }

    /// <summary>
    /// Executes the middleware pipeline for a non-streaming request.
    /// </summary>
    /// <param name="context">The execution context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The LLM response.</returns>
    public async Task<Response> InvokeAsync(LLMContext context, CancellationToken cancellationToken)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        // Build the chain from right to left (innermost to outermost)
        Func<LLMContext, Task<Response>> chain = ctx =>
            throw new InvalidOperationException("Terminal middleware was not invoked.");

        for (int i = _middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = _middlewares[i];
            var previousChain = chain;
            chain = ctx => middleware.InvokeAsync(ctx, previousChain);
        }

        return await chain(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the middleware pipeline for a streaming request.
    /// </summary>
    /// <param name="context">The execution context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async stream of response events.</returns>
    public IAsyncEnumerable<StreamEvent> InvokeStreamAsync(LLMContext context, CancellationToken cancellationToken)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        // Build the chain from right to left (innermost to outermost)
        Func<LLMContext, IAsyncEnumerable<StreamEvent>> chain = ctx =>
            throw new InvalidOperationException("Terminal middleware was not invoked.");

        for (int i = _middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = _middlewares[i];
            var previousChain = chain;
            chain = ctx => middleware.InvokeStreamAsync(ctx, previousChain);
        }

        return chain(context);
    }
}