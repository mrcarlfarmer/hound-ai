namespace Hound.Core.Models;

public enum ActivitySeverity
{
    Info,
    Warning,
    Error,
    Success
}

public class ActivityLog
{
    public string Id { get; set; } = string.Empty;
    public string PackId { get; set; } = string.Empty;
    public string HoundId { get; set; } = string.Empty;
    public string HoundName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public ActivitySeverity Severity { get; set; } = ActivitySeverity.Info;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
}
