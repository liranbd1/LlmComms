using LlmComms.Abstractions.Contracts;

namespace LlmComms.Core.Utilities;

/// <summary>
/// Shared mapping between <see cref="MessageRole"/> values and the string tokens expected by OpenAI-compatible APIs.
/// </summary>
public static class MessageRoleMapper
{
    /// <summary>
    /// Maps the supplied <see cref="MessageRole"/> to a canonical string representation.
    /// </summary>
    /// <param name="role">Role to map.</param>
    /// <param name="defaultRole">Fallback value (defaults to <c>"user"</c>).</param>
    public static string ToString(MessageRole role, string defaultRole = "user")
    {
        return role switch
        {
            MessageRole.System => "system",
            MessageRole.User => "user",
            MessageRole.Assistant => "assistant",
            MessageRole.Function => "tool",
            _ => defaultRole
        };
    }
}
