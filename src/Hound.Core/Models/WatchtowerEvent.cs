namespace Hound.Core.Models;

public class WatchtowerEvent
{
    public string Id { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string ImageName { get; set; } = string.Empty;
    public string OldImageId { get; set; } = string.Empty;
    public string NewImageId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string RawPayload { get; set; } = string.Empty;
}
