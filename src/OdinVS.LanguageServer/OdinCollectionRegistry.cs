using OdinVS.Core;

namespace OdinVS.LanguageServer;

// The one tracker of the solution's collections: the project system writes to it, the language client restarts ols when it changes
// and the package shows its Problems. Wait for 500 ms of quiet, at most 3 s, so a solution that is still loading restarts ols once.
// Public because the project system (another assembly) uses Instance.
public static class OdinCollectionRegistry
{
    public static OdinCollectionTracker Instance { get; } = new(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(3), Log.Default.Write);
}
