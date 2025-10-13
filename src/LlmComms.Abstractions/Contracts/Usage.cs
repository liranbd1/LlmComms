using System;

namespace LlmComms.Abstractions.Contracts;

public sealed record Usage(int PromptTokens, int CompletionTokens, int TotalTokens)
{
    public int PromptTokens { get; set; } = PromptTokens;
    public int CompletionTokens { get; set; } = CompletionTokens;
    public int TotalTokens { get; set; } = TotalTokens;
}