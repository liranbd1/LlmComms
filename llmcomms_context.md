# LlmComms - Development Context Document

## Project Overview
LlmComms is a production-grade .NET library for unified LLM provider communication. It provides a stable, provider-agnostic abstraction layer with built-in observability, middleware extensibility, and strict contract guarantees.

## Core Philosophy

### Design Principles
1. **Small core, deep control**: Minimal façade with maximal extension points
2. **Stable contracts**: No breaking changes to public interfaces/DTOs in minor versions after v1
3. **Provider parity**: Same public API across all vendors; use capability flags for feature detection
4. **Observability is built-in**: Logs, metrics, traces, and token usage tracking are first-class citizens
5. **Agents/graphs are optional**: Keep them separate from core; core is purely communication

### Architectural Invariants
- Public abstractions are stable; changes after v1 are additive only
- Providers and middleware must not change public contracts
- Core is transport- and provider-agnostic (no vendor lock-in)
- Deterministic behavior is preferred; strict modes available for validation
- Async-only API; cancellation is mandatory on all operations

## Architecture Pattern

**Hexagonal Architecture (Ports & Adapters)**

```
Application Code
    ↓
ILLMClient (Port)
    ↓
Middleware Chain:
  1. Tracing
  2. Redaction
  3. Logging
  4. Metrics
  5. Validator
  6. Cache
  7. Terminal → ILLMProvider (Port)
    ↓
Provider Adapter (OpenAI/Anthropic/Azure)
    ↓
SDK or REST via ITransport
```

### Extension Points
- **Providers**: Implement `ILLMProvider` for new LLM vendors
- **Middleware**: Implement `ILLMMiddleware` for request/response interception
- **Policies**: Implement `IPolicy` for retry/timeout/circuit-breaker logic
- **Transport**: Implement `ITransport` for custom HTTP behavior
- **Cache**: Pluggable caching via middleware

## Technology Stack

### Target Frameworks
**Goal**: Maximum compatibility across .NET ecosystem

- **Abstractions**: `netstandard2.0` only
  - Runs on: .NET Framework 4.6.1+, .NET Core 2.0+, .NET 5+, Mono, Xamarin
  - Maximum reach for pure contracts

- **Core & Providers**: `netstandard2.0` + `net8.0` (multi-target)
  - `netstandard2.0`: Broad compatibility (.NET Framework 4.6.1+, .NET Core 2.0+)
  - `net8.0`: Performance optimizations and modern APIs where beneficial
  - Multi-targeting allows best-of-both-worlds

- **Tests & Samples**: `net8.0` only
  - Development/testing doesn't need broad compatibility

**Supported Runtimes**:
- ✅ .NET Framework 4.6.1+ (via netstandard2.0)
- ✅ .NET Core 2.0+ (via netstandard2.0)
- ✅ .NET 5, 6, 7, 8, 9+ (via netstandard2.0 or net8.0 target)
- ✅ Mono (via netstandard2.0)
- ✅ Xamarin (via netstandard2.0)

### Development Environment
- **SDK**: .NET 9 (installed on development machine)
- **Language Version**: `latest` (C# 13 features available)
- **Compiler**: .NET 9 SDK can compile to older targets

### Language Features
- **C# Language Version**: `latest` (even for netstandard2.0 targets)
  - Modern C# features allowed (nullable reference types, records, pattern matching, init-only properties)
  - Compiler generates compatible IL for netstandard2.0
  - These are compile-time features, not runtime dependencies

- **API Surface Constraints**:
  - No public signatures depend on `Span<T>` or `Memory<T>` (netstandard2.0 doesn't have these)
  - No source generators in public API
  - Avoid APIs introduced after netstandard2.0 in public contracts
  - Use `#if` preprocessor directives when netstandard2.0 and net8.0 need different implementations

### Compatibility Strategy
- **Provider SDKs**: 
  - If vendor SDK requires modern .NET → implement REST fallback for netstandard2.0
  - REST client must work on netstandard2.0 (use HttpClient from netstandard2.0)
  
- **Dependencies**:
  - Choose libraries that support netstandard2.0
  - Avoid dependencies that only work on modern .NET in Core/Abstractions
  
- **Polyfills**:
  - Use `System.Threading.Tasks.Extensions` (>= 4.5.4) for `ValueTask` on netstandard2.0
  - Use `Microsoft.Bcl.AsyncInterfaces` for `IAsyncEnumerable<T>` on netstandard2.0
  
- **Performance**:
  - Use conditional compilation for optimized paths on net8.0
  - Example: `Span<T>` optimizations on net8.0, array-based on netstandard2.0

## Public Contracts (v1 Specification)

### Core DTOs (Data Transfer Objects)

#### LLMMessage
- **Role**: string - Message role (system, user, assistant, tool)
- **Content**: string - Message content

#### LLMRequest
- **Messages**: IReadOnlyList<LLMMessage> (required) - Conversation history
- **Tools**: ToolCollection? (optional) - Available tools/functions
- **Temperature**: double? (optional) - Sampling temperature (0.0-2.0)
- **TopP**: double? (optional) - Nucleus sampling threshold
- **MaxOutputTokens**: int? (optional) - Maximum completion tokens
- **ResponseFormat**: string? (optional) - Values: "text" | "json_object"
- **ProviderHints**: IReadOnlyDictionary<string, object>? (optional) - Per-call provider-specific overrides

#### LLMResponse
- **Output**: LLMMessage - The assistant's response message
- **Usage**: LLMUsage - Token consumption details
- **FinishReason**: string? - Why completion stopped (stop|length|tool_call)
- **ToolCalls**: IReadOnlyList<ToolCall>? (optional) - Tool invocations if any
- **ProviderRaw**: IReadOnlyDictionary<string, object>? (optional) - Raw provider data passthrough

#### LLMUsage
- **PromptTokens**: int - Input tokens consumed
- **CompletionTokens**: int - Output tokens generated
- **TotalTokens**: int - Sum of prompt + completion

#### LLMStreamEvent
- **Kind**: string - Event type: "delta" | "tool_call" | "complete" | "error"
- **TextDelta**: string? (optional) - Incremental text chunk
- **ToolCallDelta**: ToolCall? (optional) - Incremental tool call data
- **UsageDelta**: LLMUsage? (optional) - Incremental token usage
- **IsTerminal**: bool - True only on graceful completion (exactly once)

#### ToolDefinition
- **Name**: string - Tool/function name
- **Description**: string - What the tool does
- **JsonSchema**: IReadOnlyDictionary<string, object> - JSON Schema draft-07 subset for parameters

#### ToolCollection
- **Tools**: IReadOnlyList<ToolDefinition> - Collection of available tools

#### ToolCall
- **Name**: string - Tool being invoked
- **ArgumentsJson**: string - Raw JSON arguments

### Core Interfaces (Ports)

#### ILLMClient (Main Entry Point)
**Purpose**: Primary interface for application code to send requests to LLMs

**Methods**:
- `Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken ct = default)`
  - Sends a single request, waits for complete response
- `IAsyncEnumerable<LLMStreamEvent> StreamAsync(LLMRequest request, CancellationToken ct = default)`
  - Streams response chunks as they arrive

#### ILLMProvider (Provider Adapter Port)
**Purpose**: Interface each provider (OpenAI, Anthropic, etc.) must implement

**Properties**:
- `string Name` - Provider identifier (e.g., "openai", "anthropic")

**Methods**:
- `ILLMModel CreateModel(string modelId, ProviderModelOptions? options = null)`
  - Factory method to create model instances
- `Task<LLMResponse> SendAsync(ILLMModel model, LLMRequest request, ProviderCallContext context, CancellationToken ct)`
  - Execute non-streaming request
- `IAsyncEnumerable<LLMStreamEvent> StreamAsync(ILLMModel model, LLMRequest request, ProviderCallContext context, CancellationToken ct)`
  - Execute streaming request

#### ILLMModel (Model Metadata)
**Purpose**: Represents a specific LLM model's metadata

**Properties**:
- `string ModelId` - Model identifier (e.g., "gpt-4", "claude-3-opus")
- `int? MaxInputTokens` - Maximum input context window (null if unknown)
- `int? MaxOutputTokens` - Maximum output tokens (null if unknown)
- `string Format` - Model type: "chat" | "instruct" | "json"

#### ILLMMiddleware (Interceptor Port)
**Purpose**: Intercept and transform requests/responses in the pipeline

**Methods**:
- `Task<LLMResponse> InvokeAsync(LLMContext ctx, Func<LLMContext, Task<LLMResponse>> next)`
  - Non-streaming pipeline invocation
- `IAsyncEnumerable<LLMStreamEvent> InvokeStreamAsync(LLMContext ctx, Func<LLMContext, IAsyncEnumerable<LLMStreamEvent>> next)`
  - Streaming pipeline invocation

**Contract**: Must call `next()` to continue pipeline (unless short-circuiting intentionally)

#### IPolicy (Resilience Policy Port)
**Purpose**: Wrap execution with retry/timeout/circuit-breaker logic

**Methods**:
- `Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)`
  - Execute action with policy applied

#### ITransport (HTTP Abstraction Port)
**Purpose**: Abstract HTTP communication for testability and customization

**Methods**:
- `Task<object> SendAsync(object request, CancellationToken ct)`
  - Send HTTP request, return response
  - Note: Concrete transports define their own internal request/response shapes

### Context Objects

#### ProviderCallContext
**Purpose**: Per-request correlation and telemetry data

**Fields**:
- `RequestId` - string (GUID in "N" format, 32 hex chars, no hyphens)
- `Items` - IDictionary<string, object> (correlation bag for custom data)

#### LLMContext
**Purpose**: Complete execution context passed through middleware pipeline

**Fields**:
- `Provider` - ILLMProvider (which provider is being used)
- `Model` - ILLMModel (which model is being invoked)
- `Request` - LLMRequest (the request being processed)
- `CallContext` - ProviderCallContext (correlation data)
- `Options` - LlmClientOptions (client configuration)

#### LlmClientOptions
**Purpose**: Client-level configuration

**Fields**:
- `ThrowOnInvalidJson` - bool (default: true) - Strict JSON validation
- `EnableTokenUsageEvents` - bool (default: true) - Emit token metrics
- `EnableRedaction` - bool (default: true) - Redact sensitive data in logs
- `DefaultMaxOutputTokens` - int (default: 512) - Fallback max tokens
- `CoalesceFinalStreamText` - bool (default: false) - Combine final stream chunks

#### ProviderModelOptions
**Purpose**: Model-specific configuration overrides

**Fields**:
- `DefaultMaxOutputTokens` - int? - Override default max tokens for this model

### Model Selection Responsibilities

- **Callers** choose models whose features match their requests (e.g., vision inputs, tool calls, JSON mode). If the chosen model rejects a payload, the provider’s error surfaces to the caller unchanged.
- **Providers** translate vendor responses and propagate unsupported-feature errors instead of attempting to pre-validate capabilities.
- **Middleware** treats payloads generically; validators focus on schema correctness and leave feature gating to providers.

## Error Taxonomy

**Base**: `LlmException(message, requestId, statusCode?, providerCode?)`

### Exception Types
- `ValidationException` (400): `validation_error`
- `AuthorizationException` (401): `unauthorized`
- `PermissionDeniedException` (403): `forbidden`
- `QuotaExceededException` (402): `quota_exceeded`
- `RateLimitedException` (429): `rate_limited` + `RetryAfter?: TimeSpan`
- `ProviderUnavailableException` (503): `provider_unavailable`
- `TimeoutException` (null): `timeout`
- `ProviderUnknownException` (null): `unknown`

### HTTP Mapping Rules
- 400/422 → ValidationException
- 401 → AuthorizationException
- 403 → PermissionDeniedException
- 402 or quota headers → QuotaExceededException
- 429 → RateLimitedException (parse RetryAfter header)
- 5xx → ProviderUnavailableException
- TaskCanceled due to timeout → TimeoutException
- Otherwise → ProviderUnknownException

## Policies & Resilience

### Defaults
- **Timeout**: 60 seconds
- **Retries**: 2 attempts with decorrelated jitter backoff (base=250ms, cap=4s)

### Retry Strategy
**DO retry on**:
- `RateLimitedException`
- `ProviderUnavailableException`
- Transient network I/O errors

**DO NOT retry on**:
- `ValidationException`
- `AuthorizationException`
- `PermissionDeniedException`
- `QuotaExceededException`

**Per-request overrides**: Use `ProviderHints` (e.g., `timeoutMs`, `maxRetries`)

## Middleware Contract

### Ordering (Enforced)
1. Tracing
2. Redaction
3. Logging
4. Metrics
5. Validator
6. Cache
7. Terminal (Provider invocation)

### Rules
- Middleware must be re-entrant and thread-safe
- Pre-terminal middlewares may mutate `ctx.Request`
- Post-terminal middlewares read/transform response
- Validator enforces JSON mode and tool schema when configured
- Caching must honor `ProviderHints.no_cache=true`
- Terminal invokes provider `Send/Stream`

### Streaming Semantics
- `IAsyncEnumerable<LLMStreamEvent>` must preserve order of deltas
- Cancellation must be honored immediately
- Terminal event (`IsTerminal=true`) emitted exactly once on graceful completion
- No terminal event on hard cancellation

## Observability

### Logging Fields (Structured)
- `request_id`
- `provider`
- `model`
- `status`
- `latency_ms`
- `finish_reason`
- `prompt_tokens`, `completion_tokens`, `total_tokens`

### Metrics (Prometheus-style)
- `llm_requests_total{provider, model, status}`
- `llm_latency_seconds_bucket{provider, model}`
- `llm_tokens_total{provider, model, kind=prompt|completion}`
- `llm_errors_total{provider, model, code}`

### Tracing
- Create `Activity` per request
- Propagate `RequestId` as baggage
- Compatible with OpenTelemetry

### Redaction
- Default: enabled
- Mask API keys, emails, phone numbers in logs/traces
- Configurable rules

## Determinism & Validation

### Request Normalization
- Normalize `LLMRequest` for hashing (strip volatile fields)
- Expose `RequestHash` helper for cache keys and idempotency

### JSON Mode Validation
- If `ResponseFormat=json_object` and `ThrowOnInvalidJson=true`:
  - Throw `ValidationException` on invalid JSON
- If lenient: return text, set `ProviderRaw.json_invalid=true`

### Tool Validation
- Ensure `ToolCall.Name` exists in `ToolCollection`
- Parse `ArgumentsJson` against schema
- Strict mode: throw on mismatch
- Lenient mode: set `ProviderRaw.tool_mismatch=true`

## Provider Requirements

### Implementation Contract
1. Implement `ILLMProvider` once
2. Internal SDK or REST strategy is opaque (hidden from public API)
3. Propagate unsupported-feature responses from the vendor without pre-filtering
4. Normalize/translate vendor errors to error taxonomy
5. Map vendor streaming events to ordered `LLMStreamEvent` deltas
6. Provide REST fallback for netstandard2.0 compatibility

### Initial Providers
- OpenAI (GPT-4, GPT-3.5)
- Anthropic (Claude)
- Azure OpenAI

## Non-Goals (v1)

Explicitly **NOT** included in v1:
- Agent runtime in core
- Vector database or embedding store
- Prompt templating language
- UI components

*These may be separate packages in future.*

## Future Extensions (Post-v1)

### Execution Graph Package (Separate)
- Operators: `PromptOp`, `ToolOp`, `BranchOp`, `MapOp`, `JoinOp`, `DelayOp`
- Immutable, versioned state with checkpoints
- Pre-tool approval hooks, human-in-the-loop
- Reuses core `Request`/`Response`/`StreamEvent` contracts

### MCP Bridge
- Remote tools as `ToolDefinition` with consistent I/O

## Project Structure

```
LlmComms/
├── src/
│   ├── LlmComms.Abstractions/          [netstandard2.0]
│   │   ├── Contracts/                  (DTOs: Request, Response, Message, etc.)
│   │   ├── Ports/                      (Interfaces: ILLMClient, ILLMProvider, etc.)
│   │   └── Exceptions/                 (Exception hierarchy)
│   ├── LlmComms.Core/                  [netstandard2.0, net8.0]
│   │   ├── Client/                     (LlmClient implementation)
│   │   ├── Middleware/                 (Built-in middleware)
│   │   ├── Policies/                   (Retry, timeout, circuit breaker)
│   │   ├── Transport/                  (HTTP transport abstraction)
│   │   └── Utilities/                  (Hashing, normalization, etc.)
│   ├── LlmComms.Providers.OpenAI/      [netstandard2.0, net8.0]
│   ├── LlmComms.Providers.Anthropic/   [netstandard2.0, net8.0]
│   └── LlmComms.Providers.Azure/       [netstandard2.0, net8.0]
├── tests/
│   ├── LlmComms.Tests.Unit/            [net8.0]
│   └── LlmComms.Tests.Integration/     [net8.0]
├── samples/
│   ├── LlmComms.Samples.Basic/         [net8.0]
│   └── LlmComms.Samples.Advanced/      [net8.0]
├── LlmComms.sln
└── Directory.Build.props
```

## Development Dependencies

### Core Libraries
- `System.Threading.Tasks.Extensions` (>= 4.5.4) - ValueTask support for netstandard2.0
- `Microsoft.Bcl.AsyncInterfaces` (>= 6.0.0) - IAsyncEnumerable<T> for netstandard2.0
- `Microsoft.Extensions.Logging.Abstractions` - Logging façade (netstandard2.0 compatible)
- `System.Diagnostics.DiagnosticSource` (>= 6.0.0) - Activity/tracing (netstandard2.0 compatible)

### Testing
- `xUnit` (>= 2.4.2) - Test framework
- `FluentAssertions` (>= 6.11.0) - Assertion library
- `NSubstitute` (>= 5.0.0) - Mocking framework
- `Microsoft.NET.Test.Sdk` - Test host

### Provider SDKs (Will be added per provider)
- OpenAI SDK (or REST fallback for netstandard2.0)
- Anthropic SDK (or REST fallback for netstandard2.0)
- Azure.AI.OpenAI (check netstandard2.0 compatibility)

## Acceptance Criteria (v1)

✅ **Before v1 release**:
1. Compiles for netstandard2.0 and net8.0
2. Public types/names match spec exactly
3. Middleware ordering enforced
4. Policy defaults: timeout=60s, retries=2 with jitter
5. Capability-agnostic flow verified (providers surface unsupported-feature errors)
6. Streaming preserves order; terminal event semantics honored
7. Error taxonomy thrown as specified
8. No vendor or transport dependencies leak into Abstractions
9. At least one provider fully implemented (OpenAI recommended)
10. Unit test coverage >80%
11. Integration tests with real provider (optional API key)
12. Documentation: README, API docs, migration guide

## Development Roadmap

### Phase 1: Abstractions Layer
**Goal**: Define all public contracts with zero implementation

**Tasks**:
1. Create DTOs in `src/LlmComms.Abstractions/Contracts/`:
   - LLMMessage, LLMRequest, LLMResponse
   - LLMUsage, LLMStreamEvent
   - ToolDefinition, ToolCollection, ToolCall
   - ProviderCallContext, LLMContext, LlmClientOptions
   - ProviderModelOptions

2. Create interfaces in `src/LlmComms.Abstractions/Ports/`:
   - ILLMClient
   - ILLMProvider
   - ILLMModel
   - ILLMMiddleware
   - IPolicy
   - ITransport

3. Create exceptions in `src/LlmComms.Abstractions/Exceptions/`:
   - LlmException (base)
   - ValidationException
   - AuthorizationException
   - PermissionDeniedException
   - QuotaExceededException
   - RateLimitedException (with RetryAfter property)
   - ProviderUnavailableException
   - TimeoutException
   - ProviderUnknownException

**Deliverable**: Abstractions project compiles with full XML documentation

### Phase 2: Core Infrastructure
**Goal**: Implement the middleware pipeline and client orchestration

**Tasks**:
1. Create middleware pipeline in `src/LlmComms.Core/Middleware/`:
   - MiddlewareChain (orchestrator)
   - TerminalMiddleware (invokes provider)

2. Create policies in `src/LlmComms.Core/Policies/`:
   - TimeoutPolicy
   - RetryPolicy (decorrelated jitter backoff)
   - CompositePolicy (combines multiple policies)

3. Create client in `src/LlmComms.Core/Client/`:
   - LlmClient (implements ILLMClient)
   - LlmClientBuilder (fluent configuration)

4. Create utilities in `src/LlmComms.Core/Utilities/`:
   - RequestNormalizer (for hashing/caching)
   - RequestHasher (deterministic hash generation)
   - GuidHelper (RequestId generation)

**Deliverable**: Core project compiles; pipeline can be constructed and invoked

### Phase 3: Observability Middleware
**Goal**: Add logging, metrics, tracing, and redaction

**Tasks**:
1. TracingMiddleware - Create Activity per request, propagate RequestId
2. RedactionMiddleware - Mask sensitive data (API keys, emails, phones)
3. LoggingMiddleware - Structured logging with all required fields
4. MetricsMiddleware - Emit counters and histograms

**Deliverable**: Full observability stack in middleware chain

### Phase 4: Validation & Caching Middleware
**Goal**: Add request validation and response caching

**Tasks**:
1. ValidatorMiddleware:
   - Validate JSON mode responses
   - Validate tool calls against schema
   - Check capabilities before execution

2. CacheMiddleware:
   - Interface: ILLMCache
   - In-memory implementation
   - Honor ProviderHints.no_cache

**Deliverable**: Complete middleware stack operational

### Phase 5: First Provider (OpenAI)
**Goal**: Full OpenAI provider implementation

**Tasks**:
1. Create OpenAIProvider in `src/LlmComms.Providers.OpenAI/`:
   - Implement ILLMProvider
   - Create OpenAIModel (implements ILLMModel)
   - Map OpenAI model names (gpt-4, gpt-3.5-turbo, etc.)

2. SDK Adapter:
   - Use official OpenAI SDK for net8.0
   - Translate SDK requests/responses to/from LLM contracts

3. REST Fallback:
   - HTTP client for netstandard2.0
   - Manual JSON serialization
   - Map REST responses to LLM contracts

4. Error Translation:
   - Map OpenAI error codes to exception taxonomy
   - Extract RetryAfter from 429 responses

5. Streaming:
   - Convert OpenAI SSE stream to LLMStreamEvent sequence
   - Ensure IsTerminal=true on graceful completion

**Deliverable**: OpenAI provider fully functional with tests

### Phase 6: Additional Providers
**Goal**: Anthropic and Azure providers

**Tasks**:
1. AnthropicProvider (Claude models)
2. AzureProvider (Azure OpenAI Service)
3. Follow same pattern as Phase 5

**Deliverable**: Multi-provider support verified

### Phase 7: Testing Strategy
**Goal**: Comprehensive test coverage

**Tasks**:
1. Unit Tests (`tests/LlmComms.Tests.Unit/`):
   - Middleware pipeline ordering
   - Policy behavior (retry, timeout)
   - Request normalization
   - Error taxonomy mapping
   - Validation logic

2. Integration Tests (`tests/LlmComms.Tests.Integration/`):
   - Real provider calls (with API keys)
   - Streaming behavior
   - Tool calling
   - Error scenarios

3. Test Doubles:
   - FakeProvider (predictable responses)
   - MockTransport (no network)

**Deliverable**: >80% code coverage

### Phase 8: Samples & Documentation
**Goal**: Usable examples and comprehensive docs

**Tasks**:
1. Basic Sample (`samples/LlmComms.Samples.Basic/`):
   - Simple chat completion
   - Configuration examples
   - Error handling

2. Advanced Sample (`samples/LlmComms.Samples.Advanced/`):
   - Streaming responses
   - Tool calling
   - Custom middleware
   - Retry policies
   - Multi-provider switching

3. Documentation:
   - README with quick start
   - API documentation (XML → DocFX)
   - Architecture diagrams
   - Migration guide (if applicable)

**Deliverable**: Production-ready v1.0.0

## Implementation Guidelines

### Coding Standards
- **Nullable reference types**: Enabled everywhere; no null warnings allowed
- **XML documentation**: Required for all public types and members
- **Warnings as errors**: Enforced via `TreatWarningsAsErrors=true`
- **Async patterns**: 
  - Use `ConfigureAwait(false)` in library code
  - Never block on async code (no `.Result` or `.Wait()`)
- **Cancellation**: 
  - Honor `CancellationToken` in all async methods
  - Pass it through entire call chain
  - Check `ct.ThrowIfCancellationRequested()` at yield points
- **Immutability**: 
  - DTOs use init-only properties or records
  - Use `IReadOnlyList` / `IReadOnlyDictionary` in public APIs
- **Thread Safety**: 
  - Middleware must be thread-safe and re-entrant
  - Use immutable data structures where possible

### Naming Conventions
- **DTOs**: PascalCase (e.g., `LLMRequest`, `LLMResponse`)
- **Interfaces**: IPascalCase (e.g., `ILLMClient`, `ILLMProvider`)
- **Private fields**: _camelCase (e.g., `_httpClient`, `_logger`)
- **Constants**: UPPER_SNAKE_CASE or PascalCase for public
- **Async methods**: Always suffix with `Async`

### Error Handling Patterns
- **Never swallow exceptions** in library code
- **Map provider errors** to taxonomy at provider boundary
- **Enrich exceptions** with RequestId and context
- **Log before throwing** (if logger available)
- **Validation failures**: Throw early, fail fast

### Testing Patterns
- **Arrange-Act-Assert** structure
- **One assertion per test** (prefer focused tests)
- **Descriptive test names**: `MethodName_Scenario_ExpectedBehavior`
- **Use test doubles**: Prefer fakes over mocks for complex behavior
- **Integration tests**: Use real APIs sparingly; gate behind environment variables

## Current Status

**✅ Completed**:
- Solution structure created
- All project files initialized
- Multi-targeting configured
- Build system working (`dotnet build` succeeds)
- Core dependencies added

**🔄 Next Steps**:
- Implement Abstractions layer (DTOs, interfaces, exceptions)
- Set up unit test structure
- Begin Core implementation

---

## How to Resume Development

### Starting a New Session

**Context Prompt for Claude**:
```
I'm continuing development of LlmComms, a .NET library for unified LLM provider 
communication. The solution structure is initialized and builds successfully.

Development Environment:
- SDK: .NET 9 (installed on my machine)
- Target Frameworks: netstandard2.0 + net8.0 (multi-targeting)
- Compatibility: Must run on .NET Framework 4.6.1+, .NET Core 2.0+, .NET 5-9+
- Language: C# latest (modern features OK, compile to netstandard2.0)

Current Status:
- Solution created at: /Users/liran/Desktop/Developement.nosync/llmComms/
- All projects compile (netstandard2.0 + net8.0 multi-targeting)
- Directory.Build.props configured
- Core dependencies added (including polyfills for netstandard2.0)

I'm ready to start Phase [X]: [Phase Name]

Please provide step-by-step instructions for implementing [specific component].
Do NOT write full implementations - give me:
1. What types/interfaces to create
2. What their contracts should be (properties, methods, signatures)
3. Key implementation notes and gotchas (especially around netstandard2.0 compatibility)
4. How to test them

Refer to the full specification in this conversation for all contract details.
```

### Phase-Specific Prompts

**Phase 1 - Abstractions**:
```
I'm starting Phase 1: Abstractions. Help me create the DTOs in 
src/LlmComms.Abstractions/Contracts/. Start with LLMMessage, LLMRequest, 
and LLMResponse. For each type, tell me:
- Should it be a class or record?
- What properties (with types)?
- Should properties be init-only or get-only?
- What XML documentation to include?
```

**Phase 2 - Core**:
```
I'm starting Phase 2: Core Infrastructure. I need to implement the middleware 
pipeline. Explain:
- How to structure the MiddlewareChain class
- How to enforce middleware ordering
- How to handle both SendAsync and StreamAsync paths
- How to pass LLMContext through the chain
```

**Phase 3 - Observability**:
```
I'm implementing observability middleware. For TracingMiddleware:
- How should I create and manage the Activity?
- What tags/baggage should I set?
- How do I propagate RequestId?
- How do I handle streaming vs non-streaming differently?
```

**Phase 5 - Provider**:
```
I'm implementing the OpenAI provider. Guide me through:
- How to structure the OpenAIProvider class
- How to handle SDK vs REST fallback
- How to map OpenAI errors to our taxonomy
- How to transform OpenAI streaming events to LLMStreamEvent
```

### Quick Reference: Where Things Live

**Abstractions** (`src/LlmComms.Abstractions/`):
- `Contracts/` - All DTOs (LLMRequest, LLMResponse, etc.)
- `Ports/` - All interfaces (ILLMClient, ILLMProvider, etc.)
- `Exceptions/` - Exception hierarchy

**Core** (`src/LlmComms.Core/`):
- `Client/` - LlmClient implementation
- `Middleware/` - Built-in middleware implementations
- `Policies/` - Retry, timeout, circuit breaker
- `Transport/` - HTTP abstraction
- `Utilities/` - Helpers (hashing, normalization)

**Providers** (`src/LlmComms.Providers.*/`):
- Each provider in its own project
- Internal SDK/REST strategy
- Error mapping
- Streaming translation

**Tests** (`tests/`):
- `LlmComms.Tests.Unit/` - Fast, isolated tests
- `LlmComms.Tests.Integration/` - Real API tests (require keys)

### Common Questions

**Q: Should I use classes or records for DTOs?**  
A: Records for immutable DTOs (LLMMessage, LLMRequest, LLMResponse). Classes for stateful objects (LlmClient, providers).

### Multi-Targeting Strategy

**When to use `#if` directives**:
```
Example: Performance-critical code with Span<T> optimization

#if NET8_0_OR_GREATER
    // Use Span<T>, stackalloc, modern APIs
    Span<byte> buffer = stackalloc byte[256];
#else
    // Use arrays, heap allocation for netstandard2.0
    byte[] buffer = new byte[256];
#endif
```

**Target Framework Symbols**:
- `NETSTANDARD2_0` - When compiling for netstandard2.0
- `NET8_0_OR_GREATER` - When compiling for net8.0 or later
- `NETFRAMEWORK` - When running on .NET Framework (not netstandard2.0)
- `NETCOREAPP` - When running on .NET Core/.NET 5+

**Guidelines**:
- Prefer netstandard2.0-compatible code when performance difference is negligible
- Use `#if` only when there's significant benefit (performance, API availability)
- Never let target-specific code leak into public API surface
- Test both target frameworks in CI/CD

**Common Polyfill Patterns**:
```
// IAsyncEnumerable<T> via Microsoft.Bcl.AsyncInterfaces
// Works on netstandard2.0, native on net8.0

// ValueTask<T> via System.Threading.Tasks.Extensions  
// Works on netstandard2.0, native on net8.0

// ConfigureAwait extensions
// Always available via polyfills
```

**Q: What if a provider SDK doesn't support netstandard2.0?**  
A: Implement REST fallback using HttpClient, which is available on netstandard2.0.

**Q: How should I test middleware?**  
A: Create a test middleware chain with fake provider, invoke, assert on ctx modifications and results.

**Q: How do I test retry policies?**  
A: Use a fake action that fails N times, then succeeds. Assert on retry count and backoff timing.

**Q: How should streaming cancellation work?**  
A: Pass CancellationToken to IAsyncEnumerable, check it in yield loops, stop iteration on cancellation, do NOT emit IsTerminal=true on cancel.

### Key Design Decisions to Remember

1. **No breaking changes after v1**: All public contracts are stable
2. **Provider-agnostic core**: No vendor types in Abstractions or Core
3. **Fail early**: Validate capabilities before calling provider
4. **Explicit > Implicit**: No magic; configuration is visible
5. **Observability first**: Tracing, logging, metrics are not optional add-ons
6. **Async all the way**: No sync over async, no blocking
7. **Cancellation everywhere**: Every async method accepts CancellationToken

---

**Ready to start?** Pick a phase and ask Claude for step-by-step instructions!

---

*Document Version: 1.0 | Last Updated: October 2025*
