using System;
using System.Collections.Generic;

namespace LlmComms.Abstractions.Contracts;

/// <summary>
/// Represents the reasoning tokens emitted by a provider during a completion.
/// </summary>
public sealed class ReasoningTrace
{
    /// <summary>
    /// Gets or sets the collection of reasoning segments in the order they were produced.
    /// </summary>
    public IReadOnlyList<ReasoningSegment> Segments { get; set; } = Array.Empty<ReasoningSegment>();

    /// <summary>
    /// Gets or sets provider-specific metadata that describes the reasoning trace (e.g., token counts).
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Represents a single chunk of reasoning text emitted by the model.
/// </summary>
public sealed class ReasoningSegment
{
    /// <summary>
    /// Gets or sets the textual content of this reasoning segment.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets optional provider-specific metadata for this segment (e.g., status).
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; set; }
}
