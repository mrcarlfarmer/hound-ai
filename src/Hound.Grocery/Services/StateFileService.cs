using Hound.Grocery.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hound.Grocery.Services;

/// <summary>
/// The five durable markdown state files that make up the grocery working store
/// (spec §8). RavenDB remains the activity/audit log; these files are the
/// human-readable working state.
/// </summary>
public enum GroceryStateFile
{
    ShoppingList,
    Preferences,
    BudgetLedger,
    BasketTrace,
    PurchaseHistory,
}

/// <summary>
/// Reads/writes the grocery markdown state files on the git-ignored data volume
/// (spec §8). Both users can inspect/edit the files directly.
/// <para>
/// <b>Phase 1 scaffold:</b> provides file-path resolution, directory bootstrap
/// and basic read/write. No parsing/serialisation of list/preference structures
/// yet (later phases).
/// </para>
/// </summary>
public class StateFileService
{
    private readonly string _dataDirectory;
    private readonly ILogger<StateFileService>? _logger;

    public StateFileService(IOptions<StateFileSettings> options, ILoggerFactory? loggerFactory = null)
    {
        _dataDirectory = options.Value.DataDirectory;
        _logger = loggerFactory?.CreateLogger<StateFileService>();
    }

    /// <summary>Absolute path to the given state file.</summary>
    public string PathFor(GroceryStateFile file) => Path.Combine(_dataDirectory, FileName(file));

    /// <summary>Ensures the data directory exists.</summary>
    public void EnsureDirectory() => Directory.CreateDirectory(_dataDirectory);

    /// <summary>Reads the raw markdown content, or an empty string if absent.</summary>
    public async Task<string> ReadAsync(GroceryStateFile file, CancellationToken cancellationToken = default)
    {
        var path = PathFor(file);
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    /// <summary>Writes raw markdown content, creating the directory if needed.</summary>
    public async Task WriteAsync(GroceryStateFile file, string content, CancellationToken cancellationToken = default)
    {
        EnsureDirectory();
        var path = PathFor(file);
        await File.WriteAllTextAsync(path, content, cancellationToken);
        _logger?.LogDebug("Wrote grocery state file {File} ({Bytes} bytes)", path, content.Length);
    }

    internal static string FileName(GroceryStateFile file) => file switch
    {
        GroceryStateFile.ShoppingList => "shopping-list.md",
        GroceryStateFile.Preferences => "preferences.md",
        GroceryStateFile.BudgetLedger => "budget-ledger.md",
        GroceryStateFile.BasketTrace => "basket-trace.md",
        GroceryStateFile.PurchaseHistory => "purchase-history.md",
        _ => throw new ArgumentOutOfRangeException(nameof(file), file, "Unknown grocery state file"),
    };
}
