namespace OdinVS.Core;

internal static class BundledOls
{
    // The ols.exe shipped in the VSIX. src/OdinVS/ols/ols.pin records the same version plus the binary's SHA-256; the
    // build and BundledOlsPinTests fail if they drift, and the tier-2 test checks the binary reports it.
    // Change it only with scripts/bump-ols.ps1.
    public static readonly OdinVersion Version = new(2026, 10);
}
