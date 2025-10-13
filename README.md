# LlmComms

LlmComms is a .NET library that provides a consistent client experience across multiple LLM providers. It follows a hexagonal architecture: application code talks to an `IClient`, requests flow through a configurable middleware pipeline, and a provider adapter performs the actual network call.

## Default Client Pipeline

When you build an `LlmClient` with `LlmClientBuilder`, the following middleware order is registered:

1. `TracingMiddleware` – creates an `Activity`, propagates `RequestId`, and captures usage tags.
2. `RedactionMiddleware` – produces sanitized previews and message copies in `ProviderCallContext.Items`.
3. `LoggingMiddleware` – emits structured start/success/failure logs, using redacted previews when available.
4. `MetricsMiddleware` – publishes counters and histograms via the `Meter` named `LlmComms`.
5. (Additional middlewares can be injected here through `ConfigureMiddleware`.)
6. `TerminalMiddleware` – calls the provider’s `SendAsync`/`StreamAsync`.

The pipeline is enforced by `MiddlewarePipelineBuilder` so the terminal middleware is always last.

## Building a Client

```csharp
var client = new LlmClientBuilder()
    .UseProvider(openAiProvider)
    .UseModel("gpt-4o-mini")
    .ConfigureOptions(options =>
    {
        options.EnableRedaction = true;      // default
        options.DefaultMaxOutputTokens = 512;
    })
    .ConfigureMiddleware(pipeline =>
    {
        // Add custom middleware before the terminal component if needed
        // pipeline.Use(new MyCustomMiddleware());
    })
    .UseLoggerFactory(loggerFactory)         // optional, defaults to NullLoggerFactory
    .Build();

var response = await client.SendAsync(request, cancellationToken);
```

### Streaming Requests

Streaming is available only when the provider advertises `ProviderCapabilities.SupportsStreaming`. Attempting to stream otherwise results in a `NotSupportedException`. The middleware pipeline still runs for streaming calls, and the logging/metrics components emit aggregated usage and duration once enumeration completes.

## Observability & Redaction

Redaction is controlled by `ClientOptions.EnableRedaction` (defaults to `true`). When enabled, the redaction middleware:

- Copies the request messages, applying regex-based masks, and stores them under `CallContext.Items["llm.redacted.messages"]`.
- Generates a 160-character preview (`"llm.redacted.preview"`), which the logging middleware uses at `Debug` level.

Providers and middleware can retrieve these sanitized artifacts instead of raw request content. The raw request is never mutated.

Metrics are emitted via OpenTelemetry `Meter` instruments:

- `llm.requests.total`
- `llm.request.duration`
- `llm.tokens.prompt`
- `llm.tokens.completion`
- `llm.tokens.total`

Each measurement includes tags such as `llm.provider`, `llm.model`, `llm.streaming`, `llm.outcome`, and optional `llm.finish_reason` / `llm.error_type`.

## Tests

Unit tests for middleware (`RedactionMiddleware`, `LoggingMiddleware`, `MetricsMiddleware`) and `LlmClient` integration live under `tests/LlmComms.Tests.Unit`. Run them with:

```bash
dotnet test tests/LlmComms.Tests.Unit/LlmComms.Tests.Unit.csproj
```

These tests use fakes via NSubstitute and a `MeterListener` harness to assert emitted metrics and logging behavior.

## Next Steps

- Implement validator and cache middleware to complete the phase 4 stack.
- Wire provider adapters (OpenAI, Anthropic, Azure) into the client builder.
- Add integration tests that execute the full pipeline against in-memory or mock providers for additional coverage.
