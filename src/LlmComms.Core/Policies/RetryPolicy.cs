using System;
using System.Threading;
using System.Threading.Tasks;
using LlmComms.Abstractions.Ports;
using LlmComms.Abstractions.Exceptions;

namespace LlmComms.Core.Policies;

/// <summary>
/// Policy that retries on transient failures with decorrelated jitter backoff.
/// </summary>
public sealed class RetryPolicy : IPolicy
{
    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;

#if NET6_0_OR_GREATER
    // Use Random.Shared on .NET 6+ (thread-safe, no allocations)
    private static readonly Random _random = Random.Shared;
#else
    // Use ThreadLocal<Random> on older platforms
    private static readonly ThreadLocal<Random> _threadLocalRandom = new ThreadLocal<Random>(
        () => new Random(Guid.NewGuid().GetHashCode())
    );

    private static Random _random => _threadLocalRandom.Value!;
#endif

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryPolicy"/> class.
    /// </summary>
    /// <param name="maxRetries">Maximum number of retry attempts (default: 2).</param>
    /// <param name="baseDelay">Base delay for exponential backoff (default: 250ms).</param>
    /// <param name="maxDelay">Maximum delay cap (default: 4 seconds).</param>
    public RetryPolicy(
        int maxRetries = 2,
        TimeSpan? baseDelay = null,
        TimeSpan? maxDelay = null)
    {
        if (maxRetries < 0)
            throw new ArgumentOutOfRangeException(nameof(maxRetries), "Max retries cannot be negative.");

        _maxRetries = maxRetries;
        _baseDelay = baseDelay ?? TimeSpan.FromMilliseconds(250);
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(4);
    }

    /// <summary>
    /// Executes an action with retry logic.
    /// </summary>
    /// <typeparam name="T">The return type.</typeparam>
    /// <param name="action">The action to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the action.</returns>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        var attempt = 0;
        var previousDelay = _baseDelay;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt < _maxRetries)
            {
                attempt++;

                // Decorrelated jitter: sleep = min(cap, random(base, previous * 3))
                var jitterDelay = TimeSpan.FromMilliseconds(
                    _random.Next(
                        (int)_baseDelay.TotalMilliseconds,
                        (int)(previousDelay.TotalMilliseconds * 3)
                    )
                );

                var delay = jitterDelay > _maxDelay ? _maxDelay : jitterDelay;
                previousDelay = delay;

                // If it's a rate limit exception with RetryAfter, respect that
                if (ex is RateLimitedException rateLimitEx && rateLimitEx.RetryAfter.HasValue)
                {
                    delay = rateLimitEx.RetryAfter.Value;
                }

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private bool IsRetryable(Exception ex)
    {
        // Retry on these exception types
        return ex is RateLimitedException
            || ex is ProviderUnavailableException
            || (ex is System.Net.Http.HttpRequestException); // Network errors
    }
}