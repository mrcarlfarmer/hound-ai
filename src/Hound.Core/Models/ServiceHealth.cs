namespace Hound.Core.Models;

public enum HealthStatus
{
    Healthy,
    Degraded,
    Unhealthy,
    Unknown
}

public class ServiceHealth
{
    public string Name { get; set; } = string.Empty;
    public HealthStatus Status { get; set; } = HealthStatus.Unknown;
    public string? Detail { get; set; }
}

public class HealthReport
{
    public HealthStatus Status { get; set; } = HealthStatus.Healthy;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<ServiceHealth> Services { get; set; } = [];
}
