using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OdinVS.LanguageServer;

// A Windows job object that kills its processes when the handle closes, so ols cannot outlive devenv.exe
// (observed in Exp: without this an orphaned ols stayed running after VS exited).
internal sealed class KillOnCloseJob : IDisposable
{
    private readonly IntPtr m_Handle;

    public KillOnCloseJob()
    {
        m_Handle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (m_Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateJobObject failed ({Marshal.GetLastWin32Error()}).");
        }

        NativeMethods.ExtendedLimitInformation info = new()
        {
            BasicLimitInformation = new NativeMethods.BasicLimitInformation { LimitFlags = NativeMethods.JobObjectLimitKillOnJobClose },
        };
        int size = Marshal.SizeOf<NativeMethods.ExtendedLimitInformation>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, buffer, false);
            if (!NativeMethods.SetInformationJobObject(m_Handle, NativeMethods.JobObjectExtendedLimitInformationClass, buffer, (uint)size))
            {
                int error = Marshal.GetLastWin32Error();
                NativeMethods.CloseHandle(m_Handle);
                throw new InvalidOperationException($"SetInformationJobObject failed ({error}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Add(Process process)
    {
        if (!NativeMethods.AssignProcessToJobObject(m_Handle, process.Handle))
        {
            throw new InvalidOperationException($"AssignProcessToJobObject failed ({Marshal.GetLastWin32Error()}).");
        }
    }

    public void Dispose() => NativeMethods.CloseHandle(m_Handle);
}
