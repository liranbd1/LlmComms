# Azure Provider Design

## Goals
- Implement `AzureOpenAIProvider` using the `Azure.AI.OpenAI` SDK with optional transport overrides. **(SDK pathways implemented; transport fallback pending)**
- Implement `AzureAIInferenceProvider` targeting Azure AI Foundry via the `Azure.AI.Inference` SDK.
- Maintain consistent behavior with existing providers: message shaping, tool support, error mapping, and streaming events. **(Shared utilities ready)**
- Support API key and Entra ID (`TokenCredential`) authentication, including custom client factories. **(Shared utilities ready)**
- Provide samples and tests demonstrating configuration and usage.

## Architecture Overview

### Shared Utilities
- Introduce Azure-specific helpers for endpoint construction, header handling, and error normalization. **Implemented `AzureProviderRequestBuilder`.**
- Reuse existing utilities: `MessageRoleMapper`, `FunctionToolDescriptorFactory`, `HttpTransportRequest`, `TransportResponseReader`, `ProviderErrorMapper`.

### Azure OpenAI Provider
- Options: resource name, deployment ID, API version, API key/credential, optional endpoint override, chat client factory, transport override. **Implemented `AzureOpenAIProviderOptions` (including transport token scope).**
- Model: wraps deployment/model identifier, format metadata. **Implemented `AzureOpenAIModel`.**
- Provider: SDK default path with cached clients; transport fallback building REST payloads. **SDK send/stream pathways integrated; transport send path now implemented, streaming fallback pending.**
- Streaming: leverage SDK streaming API, translating updates into `StreamEvent` instances. **SDK streaming translation implemented.**

### Azure AI Inference Provider
- Options: endpoint, model name/deployment, API version, API key/credential, client factory, transport override.
- Model: identifies target Foundry model/deployment. **Implemented `AzureAIInferenceModel`.**
- Provider: use `Azure.AI.Inference` clients; transport fallback hitting `/models/{model}/chat/completions` endpoint.
- Streaming: use SDK streaming if available; otherwise implement SSE reader emitting deltas and completions.

## Request & Response Mapping
- Messages: map `MessageRole` via `MessageRoleMapper` to Azure roles; ensure content arrays formatted per SDK requirements. **Helper implemented.**
- Tools: translate `ToolCollection` with `FunctionToolDescriptorFactory` into Azure-compatible `tools/functions` payloads. **Helper implemented.**
- Responses: extract text, tool calls, finish reasons, usage tokens; attach `ProviderRaw` for diagnostics. **Implemented for Azure OpenAI SDK path.**
- Errors: parse status/code/message/requestId; funnel through `ProviderErrorMapper`. *(Next milestone for transport fallback and Azure Inference.)*

## Authentication Strategy
- API key: default header injection (`api-key`). **Supported by helper and SDK creation.**
- TokenCredential: allow callers to supply `TokenCredential`; register header bearer tokens for transports. **Supported by helper.**
- Client factories: support custom SDK clients for advanced scenarios. **Supported via provider options (`ChatClientFactory`, `ConfigureClientOptions`).**

## Testing Plan
- Unit tests with fake transports verifying payload shapes, headers, and error handling. *(Pending once transport path lands.)*
- SDK substitution tests simulating successful and failure responses. *(Pending)*
- Streaming unit tests covering delta aggregation and terminal events. *(Pending)*
- Optional integration tests gated by environment variables for real Azure resources. *(Pending)*

## Samples & Documentation
- Sample console apps: `samples/LlmComms.Samples.AzureOpenAI`, `samples/LlmComms.Samples.AzureAIInference`. *(Pending)*
- Update README and provider matrix entries. *(Pending)*
- Document configuration usage and testing instructions in this file and `README`. *(Ongoing)*

## Implementation Roadmap
1. Shared Azure utilities and option scaffolding. **Completed: utilities + option/model types in place.**
2. Azure OpenAI provider implementation with tests and sample. **SDK integration complete; transport send path in place, streaming/tests/samples upcoming.**
3. Azure AI Inference provider implementation with tests and sample.
4. Integration hooks, documentation updates, and final validation.
