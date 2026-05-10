using Raven.Client.Documents;

namespace Hound.Eval;

/// <summary>
/// Creates a minimal <see cref="IDocumentStore"/> for the eval harness.
/// Uses an in-memory stub URL; sessions will only fail if actual DB operations
/// are attempted outside of dry-run mode.
/// </summary>
internal static class StubDocumentStoreFactory
{
    public static IDocumentStore Create()
    {
        var store = new DocumentStore
        {
            Urls = ["http://localhost:0"],
            Database = "eval-stub",
        };
        store.Initialize();
        return store;
    }
}
