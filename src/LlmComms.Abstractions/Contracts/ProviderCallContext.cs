namespace LlmComms.Abstractions.Contracts;

/// <summary>
/// Provides per-request correlation and telemetry data.
/// </summary>
public sealed class ProviderCallContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderCallContext"/> class.
    /// </summary>
    /// <param name="requestId">The unique request identifier (GUID in N format - 32 hex chars, no hyphens).</param>
    public ProviderCallContext(string requestId)
    {
        RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
        Items = new Dictionary<string, object>();
    }

    /// <summary>
    /// Gets the unique request identifier (GUID in N format - 32 hex chars, no hyphens).
    /// </summary>
    public string RequestId { get; }

    /// <summary>
    /// Gets a dictionary for storing custom correlation data throughout the request lifecycle.
    /// </summary>
    public IDictionary<string, object> Items { get; }
}