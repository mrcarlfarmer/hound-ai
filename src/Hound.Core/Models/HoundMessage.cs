namespace Hound.Core.Models;

public enum MessageRole
{
    System,
    User,
    Assistant,
    Tool
}

public class HoundMessage
{
    public string Id { get; set; } = string.Empty;
    public string HoundId { get; set; } = string.Empty;
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? ToolCallId { get; set; }
}
