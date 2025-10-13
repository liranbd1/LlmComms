namespace LlmComms.Core.Utilities;

/// <summary>
/// Helper for generating request identifiers.
/// </summary>
public static class GuidHelper
{
    /// <summary>
    /// Generates a new request ID in "N" format (32 hex characters, no hyphens).
    /// </summary>
    /// <returns>A request ID string.</returns>
    /// <example>
    /// Returns: "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6"
    /// </example>
    public static string NewRequestId()
    {
        return Guid.NewGuid().ToString("N");
    }
}