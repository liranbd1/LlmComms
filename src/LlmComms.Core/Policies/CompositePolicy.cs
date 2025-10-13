using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LlmComms.Abstractions.Ports;

namespace LlmComms.Core.Policies;

/// <summary>
/// Composite policy that chains multiple policies together.
/// Policies are applied in the order they are added (first = outermost).
/// </summary>
public sealed class CompositePolicy : IPolicy
{
    private readonly IReadOnlyList<IPolicy> _policies;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositePolicy"/> class.
    /// </summary>
    /// <param name="policies">The policies to combine (applied in order).</param>
    public CompositePolicy(params IPolicy[] policies)
    {
        if (policies == null || policies.Length == 0)
            throw new ArgumentException("At least one policy must be provided.", nameof(policies));

        _policies = policies.ToList();
    }

    /// <summary>
    /// Executes an action with all policies applied in sequence.
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

        // Build the chain from right to left (innermost to outermost)
        Func<CancellationToken, Task<T>> chain = action;

        for (int i = _policies.Count - 1; i >= 0; i--)
        {
            var policy = _policies[i];
            var previousChain = chain;
            chain = ct => policy.ExecuteAsync(previousChain, ct);
        }

        return await chain(cancellationToken).ConfigureAwait(false);
    }
}