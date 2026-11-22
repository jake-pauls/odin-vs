namespace OdinVS.Core;

// The one place an ODINnnnn diagnostic code is defined. The table in docs/reference/diagnostics.md lists the same codes (DiagnosticCodesTests keeps
// the table, this class and the literal in Sdk.targets from drifting). Numbering: compile errors and warnings 000x, reference and project
// problems 01xx.
public static class OdinDiagnosticCodes
{
    public const string CompileError = "ODIN0001";
    public const string CompileWarning = "ODIN0002";
    public const string BuildFailedWithoutDiagnostics = "ODIN0100";
    public const string OdinNotFound = "ODIN0101";
    public const string CircularReference = "ODIN0102";
    public const string DuplicateCollectionName = "ODIN0103";
    public const string UnresolvedReference = "ODIN0104";
    public const string NoLibraryToLink = "ODIN0105";

    // Raised by the _OdinCheckOutputType target in Sdk.targets, which can only spell it as a literal.
    public const string InvalidOutputType = "ODIN0106";
    public const string LinkPathHasSpaces = "ODIN0107";
    public const string DuplicateLibraryName = "ODIN0108";
    public const string HostCannotWalkReferences = "ODIN0109";
    public const string InvalidCollection = "ODIN0110";
}
