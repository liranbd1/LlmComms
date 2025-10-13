using System;
using System.Collections.Generic;
using LlmComms.Abstractions.Ports;
using LlmComms.Core.Middleware;
using Microsoft.Extensions.Logging;

namespace LlmComms.Core.Client;

/// <summary>
/// Builder that composes the middleware pipeline in execution order.
/// </summary>
public sealed class MiddlewarePipelineBuilder
{
    private readonly List<IMiddleware> _middlewares = new();
    private IMiddleware? _terminal;

    /// <summary>
    /// Adds a middleware to the pipeline.
    /// </summary>
    /// <param name="middleware">The middleware instance to add.</param>
    /// <returns>The current builder instance.</returns>
    public MiddlewarePipelineBuilder Use(IMiddleware middleware)
    {
        if (middleware == null)
            throw new ArgumentNullException(nameof(middleware));

        if (ReferenceEquals(middleware, _terminal))
            return this;

        if (middleware is TerminalMiddleware terminalMiddleware)
        {
            UseTerminal(terminalMiddleware);
        }
        else
        {
            _middlewares.Add(middleware);
        }

        return this;
    }

    /// <summary>
    /// Sets the terminal middleware. Only one terminal middleware can be registered.
    /// </summary>
    /// <param name="terminal">The terminal middleware instance.</param>
    /// <returns>The current builder instance.</returns>
    public MiddlewarePipelineBuilder UseTerminal(IMiddleware terminal)
    {
        if (terminal == null)
            throw new ArgumentNullException(nameof(terminal));

        _terminal = terminal;
        return this;
    }

    /// <summary>
    /// Builds the middleware chain with the configured components.
    /// </summary>
    /// <returns>A <see cref="MiddlewareChain"/> representing the pipeline.</returns>
    public MiddlewareChain Build()
    {
        if (_terminal == null)
            throw new InvalidOperationException("A terminal middleware must be registered before building the pipeline.");

        var middlewares = new List<IMiddleware>(_middlewares.Count + 1);
        middlewares.AddRange(_middlewares);
        middlewares.Add(_terminal);

        return new MiddlewareChain(middlewares);
    }

    /// <summary>
    /// Creates a builder pre-populated with the default observability middleware stack.
    /// </summary>
    /// <param name="loggerFactory">Logger factory used for creating structured logging middleware.</param>
    /// <param name="cache">Optional cache implementation for response caching.</param>
    /// <param name="cacheTtl">Optional default cache entry time-to-live.</param>
    /// <returns>A builder containing the default middleware order.</returns>
    public static MiddlewarePipelineBuilder CreateDefault(
        ILoggerFactory loggerFactory,
        ILLMCache? cache = null,
        TimeSpan? cacheTtl = null)
    {
        if (loggerFactory == null)
            throw new ArgumentNullException(nameof(loggerFactory));

        var builder = new MiddlewarePipelineBuilder();

        builder.Use(new TracingMiddleware());
        builder.Use(new RedactionMiddleware());
        builder.Use(new LoggingMiddleware(loggerFactory.CreateLogger<LoggingMiddleware>()));
        builder.Use(new MetricsMiddleware());
        builder.Use(new ValidatorMiddleware());
        if (cache != null)
        {
            builder.Use(new CacheMiddleware(cache, cacheTtl));
        }
        builder.UseTerminal(new TerminalMiddleware());

        return builder;
    }
}
