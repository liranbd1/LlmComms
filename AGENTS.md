# Repository Guidelines

## Project Structure & Module Organization
The solution `LlmComms.sln` stitches together the library projects under `src/`. `LlmComms.Core` hosts the middleware pipeline, client builder, and transport abstractions; `LlmComms.Abstractions` exposes shared contracts; provider adapters live in `LlmComms.Providers.*`. Unit tests reside in `tests/LlmComms.Tests.Unit`, while slower end-to-end scenarios belong in `tests/LlmComms.Tests.Integration`. Sample console hosts in `samples/` demonstrate client wiring. Avoid committing transient `bin/` or `obj/` output created by the .NET SDK.

## Build, Test, and Development Commands
Run `dotnet restore` once per environment to hydrate packages. `dotnet build LlmComms.sln` validates compilation with warnings treated as errors. Use `dotnet test --no-build` for fast unit and integration runs; target a project path to scope execution. `dotnet test --collect:"XPlat Code Coverage"` emits Coverlet coverage data in `TestResults/`. For quick feedback during authoring, `dotnet watch test` against the unit project keeps the loop tight.

## Coding Style & Naming Conventions
This codebase targets C# latest with nullable reference types enabled. Follow four-space indentation, expression-bodied members when they improve clarity, and PascalCase for public types/methods. Async methods should end with `Async`; options and middleware classes favour noun-based names (`RedactionMiddleware`). Before pushing, run `dotnet format` to apply the shared analyzers and keep imports tidy.

## Testing Guidelines
Write xUnit tests close to the code under test and prefer FluentAssertions for readable expectations. Mock external collaborations with NSubstitute and rely on the provided fake telemetry helpers in `tests/LlmComms.Tests.Unit`. When adding middleware or providers, cover both happy-path and failure cases, ensuring metrics and logging assertions stay deterministic. Keep coverage trending upward; new features should include regression tests before merging.

## Commit & Pull Request Guidelines
Draft commit subjects in the imperative mood (e.g., `Add metrics middleware backoff`) and keep them under 72 characters. Separate logical changes into distinct commits to ease reviews. Pull requests should include a summary, testing notes, and any relevant issue links. Attach screenshots or logs when behaviour changes observability output. Request review from a maintainer familiar with the affected area and wait for green CI before merging.
