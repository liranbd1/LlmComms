using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LlmComms.Abstractions.Contracts;
using LlmComms.Abstractions.Ports;
using LlmComms.Core.Middleware;
using LlmComms.Core.Utilities;

namespace LlmComms.Core.Client;

/// <summary>
/// Default implementation of <see cref="IClient"/> that executes requests through the middleware pipeline.
/// </summary>
public sealed class LlmClient : IClient
{
    private readonly IProvider _provider;
    private readonly IModel _model;
    private readonly MiddlewareChain _middlewareChain;
    private readonly ClientOptions _options;

    internal LlmClient(
        IProvider provider,
        IModel model,
        MiddlewareChain middlewareChain,
        ClientOptions options)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _middlewareChain = middlewareChain ?? throw new ArgumentNullException(nameof(middlewareChain));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public Task<Response> SendAsync(Request request, CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var preparedRequest = EnsureDefaultConfiguration(request);
        var context = CreateContext(preparedRequest, cancellationToken);

        return _middlewareChain.InvokeAsync(context, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<StreamEvent> StreamAsync(Request request, CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (!_provider.Capabilities.SupportsStreaming)
            throw new NotSupportedException($"Provider '{_provider.Name}' does not support streaming.");

        var preparedRequest = EnsureDefaultConfiguration(request);
        var context = CreateContext(preparedRequest, cancellationToken);

        return _middlewareChain.InvokeStreamAsync(context, cancellationToken);
    }

    private LLMContext CreateContext(Request request, CancellationToken cancellationToken)
    {
        var callContext = new ProviderCallContext(GuidHelper.NewRequestId());
        return new LLMContext(
            _provider,
            _model,
            request,
            callContext,
            _options,
            cancellationToken);
    }

    private Request EnsureDefaultConfiguration(Request request)
    {
        if (_options.DefaultMaxOutputTokens > 0 && !request.MaxOutputTokens.HasValue)
        {
            return request with { MaxOutputTokens = _options.DefaultMaxOutputTokens };
        }

        return request;
    }
}
