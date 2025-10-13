using LlmComms.Abstractions.Contracts;

namespace LlmComms.Abstractions.Ports;

/// <summary>
/// Defines middleware that can intercept and transform requests and responses in the pipeline.
/// </summary>
public interface IMiddleware
{
    /// <summary>
    /// Invokes the middleware for a non-streaming request.
    /// </summary>
    /// <param name="context">The execution context.</param>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <returns>The LLM response.</returns>
    Task<Response> InvokeAsync(
        LLMContext context,
        Func<LLMContext, Task<Response>> next);

    /// <summary>
    /// Invokes the middleware for a streaming request.
    /// </summary>
    /// <param name="context">The execution context.</param>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <returns>An async stream of response events.</returns>
    IAsyncEnumerable<StreamEvent> InvokeStreamAsync(
        LLMContext context,
        Func<LLMContext, IAsyncEnumerable<StreamEvent>> next);
}