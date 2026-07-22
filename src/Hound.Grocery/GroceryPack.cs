namespace Hound.Grocery;

/// <summary>
/// Pack-wide identity constants for the Grocery Pack. Mirrors the trading pack's
/// kebab-case <c>PackId</c> convention.
/// </summary>
public static class GroceryPack
{
    /// <summary>Kebab-case pack identifier used in activity logs and registration.</summary>
    public const string PackId = "grocery-pack";

    /// <summary>Human-readable display name.</summary>
    public const string DisplayName = "Grocery Pack";

    /// <summary>RavenDB database name for grocery documents.</summary>
    public const string Database = "hound-grocery-pack";
}
