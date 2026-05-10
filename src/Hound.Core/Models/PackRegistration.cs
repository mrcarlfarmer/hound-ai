namespace Hound.Core.Models;

/// <summary>
/// Request payload sent by pack containers to register themselves with the API on startup.
/// </summary>
public class PackRegistration
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<PackRegistrationHound> Hounds { get; set; } = [];
}

public class PackRegistrationHound
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
