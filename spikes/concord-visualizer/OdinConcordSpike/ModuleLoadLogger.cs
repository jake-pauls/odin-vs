using System;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;

namespace OdinConcordSpike;

// B1/B2: proves the component loads on a debug session and records what the debugger knows about each module
// (notably the compiler and language ids the Odin PDB yields). Notification only; it changes no behavior.
// Public because the Concord dispatcher instantiates it by name from the .vsdconfig.
public sealed class ModuleLoadLogger : IDkmModuleInstanceLoadNotification
{
    public ModuleLoadLogger()
    {
        SpikeLog.Write("ModuleLoadLogger constructed (component loaded)");
    }

    public void OnModuleInstanceLoad(DkmModuleInstance moduleInstance, DkmWorkList workList, DkmEventDescriptorS eventDescriptor)
    {
        try
        {
            var module = moduleInstance.Module;
            string compiler = module?.CompilerId is null
                ? "(none)"
                : $"vendor={module.CompilerId.VendorId} language={module.CompilerId.LanguageId}";
            SpikeLog.Write($"module load: {moduleInstance.FullName} | symbols={(module is null ? "no" : "yes")} | compilerId: {compiler} | module.Flags={module?.Flags} | instance.Flags={moduleInstance.Flags}");
        }
        catch (Exception ex)
        {
            SpikeLog.Write($"module load logging failed: {ex}");
        }
    }
}
