using System;
using LlmComms.Abstractions.Contracts;
using LlmComms.Abstractions.Ports;
using LlmComms.Core.Middleware;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LlmComms.Core.Client;

/// <summary>
/// Fluent builder for constructing <see cref="LlmClient"/> instances.
/// </summary>
public sealed class LlmClientBuilder
{
    private IProvider? _provider;
    private string? _modelId;
    private ProviderModelOptions? _modelOptions;
    private readonly ClientOptions _options = new();
    private ILoggerFactory? _loggerFactory;
    private Action<MiddlewarePipelineBuilder>? _configureMiddleware;
    private ILLMCache? _cache;
    private TimeSpan? _cacheTtl;

    /// <summary>
    /// Specifies the provider that the client should use.
    /// </summary>
    public LlmClientBuilder UseProvider(IProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        return this;
    }

    /// <summary>
    /// Specifies the model identifier and optional model options.
    /// </summary>
    public LlmClientBuilder UseModel(string modelId, ProviderModelOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("Model identifier must be provided.", nameof(modelId));

        _modelId = modelId;
        _modelOptions = options;
        return this;
    }

    /// <summary>
    /// Allows customization of client options.
    /// </summary>
    public LlmClientBuilder ConfigureOptions(Action<ClientOptions> configure)
    {
        if (configure == null)
            throw new ArgumentNullException(nameof(configure));

        configure(_options);
        return this;
    }

    /// <summary>
    /// Provides a hook to customize the middleware pipeline.
    /// </summary>
    public LlmClientBuilder ConfigureMiddleware(Action<MiddlewarePipelineBuilder> configure)
    {
        if (configure == null)
            throw new ArgumentNullException(nameof(configure));

        _configureMiddleware += configure;
        return this;
    }

    /// <summary>
    /// Supplies the logger factory used by the built-in middleware components.
    /// </summary>
    public LlmClientBuilder UseLoggerFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        return this;
    }

    /// <summary>
    /// Enables response caching with the provided cache implementation.
    /// </summary>
    /// <param name="cache">The cache implementation to use.</param>
    /// <param name="defaultTtl">Optional default time-to-live for cache entries.</param>
    public LlmClientBuilder UseCache(ILLMCache cache, TimeSpan? defaultTtl = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _cacheTtl = defaultTtl;
        return this;
    }

    /// <summary>
    /// Builds a new <see cref="LlmClient"/> instance.
    /// </summary>
    public IClient Build()
    {
        var provider = _provider ?? throw new InvalidOperationException("Provider must be configured before building the client.");
        var modelId = _modelId ?? throw new InvalidOperationException("Model identifier must be configured before building the client.");

        var model = provider.CreateModel(modelId, _modelOptions);
        var loggerFactory = _loggerFactory ?? NullLoggerFactory.Instance;

        var pipelineBuilder = MiddlewarePipelineBuilder.CreateDefault(loggerFactory, _cache, _cacheTtl);
        _configureMiddleware?.Invoke(pipelineBuilder);
        var middlewareChain = pipelineBuilder.Build();

        var optionsSnapshot = CloneOptions(_options);

        return new LlmClient(
            provider,
            model,
            middlewareChain,
            optionsSnapshot);
    }

    private static ClientOptions CloneOptions(ClientOptions source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        return new ClientOptions
        {
            ThrowOnInvalidJson = source.ThrowOnInvalidJson,
            EnableTokenUsageEvents = source.EnableTokenUsageEvents,
            EnableRedaction = source.EnableRedaction,
            DefaultMaxOutputTokens = source.DefaultMaxOutputTokens,
            CoalesceFinalStreamText = source.CoalesceFinalStreamText
        };
    }
}
