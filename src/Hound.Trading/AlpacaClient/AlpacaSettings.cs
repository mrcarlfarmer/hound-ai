namespace Hound.Trading.AlpacaClient;

public class AlpacaSettings
{
    public const string SectionName = "Alpaca";

    public string ApiKeyId { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Paper (default) or Live.</summary>
    public string Environment { get; set; } = "Paper";
}
