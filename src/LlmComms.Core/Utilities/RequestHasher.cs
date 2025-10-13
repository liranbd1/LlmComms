using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LlmComms.Abstractions.Contracts;

namespace LlmComms.Core.Utilities;

/// <summary>
/// Generates deterministic hashes for requests (used for caching and idempotency).
/// </summary>
public static class RequestHasher
{
    /// <summary>
    /// Computes a deterministic SHA256 hash of a request.
    /// </summary>
    /// <param name="request">The request to hash.</param>
    /// <returns>A hex-encoded SHA256 hash string.</returns>
    public static string ComputeHash(Request request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        // Normalize first to ensure deterministic hashing
        var normalized = RequestNormalizer.Normalize(request);

        // Serialize to JSON with deterministic options
        var json = JsonSerializer.Serialize(normalized, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        // Compute SHA256 hash
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));

        // Convert to hex string
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }
}