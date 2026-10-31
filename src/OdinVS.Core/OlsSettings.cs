namespace OdinVS.Core;

// The inputs to ols.json. M2 fills this with defaults; a later milestone can fill it from .odinproj properties.
internal sealed record OlsSettings(
    string OdinCommand,
    IReadOnlyList<string> CheckerPaths,
    IReadOnlyList<KeyValuePair<string, string>> Collections);
