using System.Collections.Immutable;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.References;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace OdinVS.ProjectSystem;

// Public because CPS discovers it through MEF. VS's default checker prompts "not a valid reference for this type of project" for every
// reference between project types it does not know, so this one (higher order = asked first) accepts Odin and native C/C++ projects.
// Circular references are rejected afterwards by VS itself: BuildDependencyProjectReferencesService.AddAsync runs the circular check
// that comes with the ProjectReferences capability (observed in the decompiled VS 18.11 project system).
[Export(typeof(IValidProjectReferenceChecker))]
[AppliesTo("OdinProject")]
[Order(1000)]
public sealed class OdinValidProjectReferenceChecker : IValidProjectReferenceChecker
{
    private static readonly string[] s_SupportedExtensions = [".odinproj", ".vcxproj"];

    // IVsProjectReference is a COM object: it is read on the UI thread, as VS's own checker does.
    public async Task<SupportedCheckResult> CanAddProjectReferenceAsync(object referencedProject)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        return Check(referencedProject);
    }

    public async Task<CanAddProjectReferencesResult> CanAddProjectReferencesAsync(IImmutableSet<object> referencedProjects)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        ImmutableDictionary<object, SupportedCheckResult>.Builder results = ImmutableDictionary.CreateBuilder<object, SupportedCheckResult>();
        foreach (object referencedProject in referencedProjects)
        {
            results[referencedProject] = Check(referencedProject);
        }

        return new CanAddProjectReferencesResult(results.ToImmutable(), null);
    }

    public Task<SupportedCheckResult> CanBeReferencedAsync(object referencingProject) => Task.FromResult(SupportedCheckResult.Supported);

    // Unknown (not NotSupported) for anything else, so the remaining checkers and VS's own prompt still decide.
    private static SupportedCheckResult Check(object referencedProject)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (referencedProject is IVsProjectReference reference && s_SupportedExtensions.Contains(Path.GetExtension(reference.FullPath), StringComparer.OrdinalIgnoreCase))
        {
            return SupportedCheckResult.Supported;
        }

        return SupportedCheckResult.Unknown;
    }
}
