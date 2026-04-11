namespace Hound.Core.Models;

public enum HoundStatus
{
    Idle,
    Processing,
    Error,
    Disabled
}

public class HoundInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PackId { get; set; } = string.Empty;
    public HoundStatus Status { get; set; } = HoundStatus.Idle;
    public DateTime? LastActivity { get; set; }
}
