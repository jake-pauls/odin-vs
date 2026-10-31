using System.Security.Cryptography;

namespace OdinVS.Core.Tests;

// Tier 1 (no toolchain needed): the shipped ols.exe is the one the pin file and BundledOls.Version describe.
// The same hash is checked at build time by the VerifyBundledOls target in OdinVS.csproj.
[TestClass]
public sealed class BundledOlsPinTests
{
    private static readonly string m_PinPath = Path.Combine(RepoPaths.BundledOlsDirectory, "ols.pin");

    [TestMethod]
    public void BundledOlsExeMatchesThePinnedHash()
    {
        string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(RepoPaths.BundledOlsDirectory, "ols.exe"))));
        Assert.AreEqual(
            ReadPin("sha256"),
            actual,
            ignoreCase: true,
            "ols.exe changed without updating ols.pin; run scripts/bump-ols.ps1 if that was intended, otherwise restore the file.");
    }

    [TestMethod]
    public void PinnedVersionMatchesBundledOlsVersion()
    {
        Assert.AreEqual(
            BundledOls.Version,
            OdinVersion.Parse(ReadPin("version")),
            "ols.pin and BundledOls.Version disagree; run scripts/bump-ols.ps1 to change them together.");
    }

    private static string ReadPin(string key)
    {
        string prefix = key + "=";
        foreach (string line in File.ReadAllLines(m_PinPath))
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return line[prefix.Length..].Trim();
            }
        }

        throw new InvalidOperationException($"'{key}' is missing from {m_PinPath}.");
    }
}
