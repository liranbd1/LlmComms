using System;
using System.Threading;
using System.Threading.Tasks;
using LlmComms.Abstractions.Ports;
using llmExceptions = LlmComms.Abstractions.Exceptions;

namespace LlmComms.Core.Policies;

/// <summary>
/// Policy that enforces a timeout on execution.
/// </summary>
public sealed class TimeoutPolicy : IPolicy
{
    private readonly TimeSpan _timeout;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeoutPolicy"/> class.
    /// </summary>
    /// <param name="timeout">The timeout duration.</param>
    public TimeoutPolicy(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");

        _timeout = timeout;
    }

    /// <summary>
    /// Executes an action with timeout enforcement.
    /// </summary>
    /// <typeparam name="T">The return type.</typeparam>
    /// <param name="action">The action to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the action.</returns>
    /// <exception cref="llmExceptions.TimeoutException">Thrown when the timeout is exceeded.</exception>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token
        );

        try
        {
            return await action(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            // Timeout occurred
            throw new llmExceptions.TimeoutException(
                $"Operation timed out after {_timeout.TotalSeconds} seconds.",
                requestId: null
            );
        }
        catch (OperationCanceledException)
        {
            // User cancellation
            throw;
        }
    }
}