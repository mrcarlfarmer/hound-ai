namespace Hound.Core.Models;

public enum PackStatus
{
    Idle,
    Running,
    Error,
    Stopped
}

public class Pack
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public PackStatus Status { get; set; } = PackStatus.Idle;
    public int HoundCount { get; set; }
    public DateTime? LastActivity { get; set; }
    public List<string> HoundIds { get; set; } = [];
}
