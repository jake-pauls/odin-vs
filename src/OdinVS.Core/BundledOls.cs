namespace OdinVS.Core;

internal static class BundledOls
{
    // The ols.exe shipped in the VSIX. A tier-2 test checks the binary still reports this version; update both on a bump.
    public static readonly OdinVersion Version = new(2026, 10);
}
