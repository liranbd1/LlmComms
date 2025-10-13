namespace LlmComms.Abstractions.Ports;

/// <summary>
/// Defines a resilience policy that can wrap execution with retry, timeout, or circuit-breaker logic.
/// </summary>
public interface IPolicy
{
    /// <summary>
    /// Executes an action with the policy applied.
    /// </summary>
    /// <typeparam name="T">The return type of the action.</typeparam>
    /// <param name="action">The action to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the action.</returns>
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken);
}