namespace Hound.Grocery.Config;

/// <summary>
/// Pay-cycle-anchored budget configuration (spec §9). Treated as a <i>soft</i>
/// cap: the human completes checkout, so the pack adds items, flags projected
/// overages and asks via Telegram.
/// </summary>
public class BudgetSettings
{
    public const string SectionName = "Budget";

    public string Currency { get; set; } = "GBP";

    /// <summary>Day of the month the pay cycle begins (e.g. payday).</summary>
    public int CycleStartDay { get; set; } = 25;

    public decimal MonthlyCap { get; set; } = 600.00m;
    public decimal WeeklyTarget { get; set; } = 140.00m;

    /// <summary>Per-week flex allowance, as a percentage (±).</summary>
    public int WeeklyFlexPercent { get; set; } = 10;

    /// <summary>Behaviour on overage. Currently only <c>FlagAndAsk</c>.</summary>
    public string OnOverage { get; set; } = "FlagAndAsk";
}

/// <summary>
/// Telegram bot configuration (spec §13). Long-polling; no webhook.
/// <para>The bot token lives in the git-ignored secrets file, not here.</para>
/// </summary>
public class TelegramSettings
{
    public const string SectionName = "Telegram";

    /// <summary>Bot token — supplied via env/secrets, never committed.</summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>The only chat/user IDs allowed to interact; all others ignored.</summary>
    public List<long> AllowedChatIds { get; set; } = [];

    /// <summary>Configurable persona name.</summary>
    public string PersonaName { get; set; } = "Sous";

    /// <summary>Persona tone — e.g. "friendly", "efficient".</summary>
    public string PersonaTone { get; set; } = "friendly, efficient, not overly talkative";
}

/// <summary>
/// Sainsbury's account + site configuration. Credentials live in the git-ignored
/// secrets file, never here (R8).
/// </summary>
public class SainsburysSettings
{
    public const string SectionName = "Sainsburys";

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string GroceriesUrl { get; set; } = "https://www.sainsburys.co.uk/gol-ui/groceries";
}

/// <summary>
/// Scheduled "shop day" configuration (spec §7.2).
/// </summary>
public class ScheduleSettings
{
    public const string SectionName = "Schedule";

    /// <summary>Day of week the basket build runs (e.g. "Friday").</summary>
    public string ShopDay { get; set; } = "Friday";

    /// <summary>Local time of day the basket build runs (HH:mm).</summary>
    public string ShopTime { get; set; } = "09:00";
}

/// <summary>
/// Ollama model selection for the grocery pack (spec §12). Both
/// <see cref="VisionModel"/> and <see cref="DefaultModel"/> default to the
/// unified multimodal <c>gemma4:12b</c>; <see cref="EmbeddingModel"/> defaults
/// to <c>embeddinggemma</c>.
/// </summary>
public class GroceryOllamaSettings
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://ollama:11434/v1";
    public string DefaultModel { get; set; } = "gemma4:12b";
    public string VisionModel { get; set; } = "gemma4:12b";
    public string EmbeddingModel { get; set; } = "embeddinggemma";
}

/// <summary>
/// Local markdown state-file configuration. The directory lives on a
/// git-ignored host volume (spec §8).
/// </summary>
public class StateFileSettings
{
    public const string SectionName = "StateFiles";

    /// <summary>Directory holding the markdown working store (mounted volume).</summary>
    public string DataDirectory { get; set; } = "/data/grocery";
}

/// <summary>
/// Configuration for the <c>grocery-browser</c> Python sidecar.
/// </summary>
public class BrowserWorkerSettings
{
    public const string SectionName = "BrowserWorker";

    /// <summary>Base URL of the sidecar on <c>hound-net</c>.</summary>
    public string BaseUrl { get; set; } = "http://grocery-browser:8090";

    /// <summary>
    /// Dry-run kill switch: when <c>true</c> the sidecar runs end-to-end without
    /// mutating the live basket (spec §11.4).
    /// </summary>
    public bool DryRun { get; set; } = true;
}

/// <summary>
/// Tunables for LearnerHound's preference learning (spec §10). All learning is
/// deterministic; these thresholds govern how quickly confidence accrues and when
/// an item is promoted to favourite/staple.
/// </summary>
public class LearnerSettings
{
    public const string SectionName = "Learner";

    /// <summary>Confidence gained per consistent observation (clamped to <see cref="MaxConfidence"/>).</summary>
    public double ConfidenceStep { get; set; } = 0.2;

    /// <summary>Upper bound on a learned confidence score.</summary>
    public double MaxConfidence { get; set; } = 1.0;

    /// <summary>
    /// Below this confidence an existing mapping may be re-learned (its preferred
    /// product replaced) by new evidence. At/above it the existing product is kept
    /// so confident or human-curated entries are never silently overwritten.
    /// </summary>
    public double RelearnBelowConfidence { get; set; } = 0.5;

    /// <summary>Total observations of an item before it is promoted to a favourite.</summary>
    public int FavouriteThreshold { get; set; } = 3;

    /// <summary>Distinct purchase dates for an item before it is promoted to a staple.</summary>
    public int StapleThreshold { get; set; } = 4;
}
