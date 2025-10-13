using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LlmComms.Abstractions.Contracts;
using LlmComms.Abstractions.Ports;

namespace LlmComms.Core.Middleware;

/// <summary>
/// Middleware that prepares redacted representations of requests for downstream telemetry.
/// The original request is left untouched; sanitized data is stored in the call context.
/// </summary>
public sealed class RedactionMiddleware : IMiddleware
{
    internal const string RedactedMessagesKey = "llm.redacted.messages";
    internal const string RedactedPreviewKey = "llm.redacted.preview";

    private const int PreviewMaxLength = 160;

    private static readonly (Regex Pattern, string Replacement)[] _redactionRules = new[]
    {
        (new Regex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled | RegexOptions.CultureInvariant), "***@***"),
        (new Regex(@"\+?\d[\d\s\-\(\)]{6,}\d", RegexOptions.Compiled | RegexOptions.CultureInvariant), "***-REDACTED-PHONE***"),
        (new Regex(@"(?i)(api[_\-\s]?key|secret|token)[^A-Za-z0-9]*[A-Za-z0-9\-_=]{8,}", RegexOptions.Compiled | RegexOptions.CultureInvariant), "***REDACTED-CREDENTIAL***")
    };

    /// <inheritdoc />
    public async Task<Response> InvokeAsync(
        LLMContext context,
        Func<LLMContext, Task<Response>> next)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        if (next == null)
            throw new ArgumentNullException(nameof(next));

        EnsureRedactedArtifacts(context);

        return await next(context).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamEvent> InvokeStreamAsync(
        LLMContext context,
        Func<LLMContext, IAsyncEnumerable<StreamEvent>> next)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        if (next == null)
            throw new ArgumentNullException(nameof(next));

        EnsureRedactedArtifacts(context);

        await foreach (var streamEvent in next(context).ConfigureAwait(false))
        {
            yield return streamEvent;
        }
    }

    private static void EnsureRedactedArtifacts(LLMContext context)
    {
        if (context.CallContext.Items.ContainsKey(RedactedPreviewKey))
            return;

        var messages = context.Request.Messages;
        if (messages == null || messages.Count == 0)
            return;

        IReadOnlyList<Message> previewSource = messages;

        if (context.Options.EnableRedaction)
        {
            var redactedMessages = messages
                .Select(message => new Message(
                    message.Role,
                    Redact(message.Content)))
                .ToList();

            context.CallContext.Items[RedactedMessagesKey] = redactedMessages;
            previewSource = redactedMessages;
        }

        var preview = BuildPreview(previewSource);
        if (!string.IsNullOrEmpty(preview))
        {
            context.CallContext.Items[RedactedPreviewKey] = preview;
        }
    }

    private static string BuildPreview(IReadOnlyList<Message> messages)
    {
        if (messages.Count == 0)
            return string.Empty;

        var snippets = new List<string>(capacity: 2);
        if (messages.Count >= 2)
            snippets.Add(SanitizeSnippet(messages[messages.Count - 2].Content));

        snippets.Add(SanitizeSnippet(messages[messages.Count - 1].Content));

        return string.Join(" | ", snippets).Trim();
    }

    private static string SanitizeSnippet(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var trimmed = content!.Replace("\r", " ").Replace("\n", " ").Trim();
        if (trimmed.Length > PreviewMaxLength)
            trimmed = trimmed.Substring(0, PreviewMaxLength);

        return trimmed;
    }

    private static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var result = input!;
        foreach (var (pattern, replacement) in _redactionRules)
        {
            result = pattern.Replace(result, replacement);
        }

        return result ?? string.Empty;
    }
}
