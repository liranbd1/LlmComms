namespace LlmComms.Abstractions.Contracts;

public enum MessageRole
{
    System,
    User,
    Assistant,
    Function
}

public sealed record Message(MessageRole Role, string Content)
{
    public MessageRole Role { get; set; } = Role;
    public string Content { get; set; } = Content;
}