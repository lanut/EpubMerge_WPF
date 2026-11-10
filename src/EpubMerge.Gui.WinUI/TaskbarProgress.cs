using System.Runtime.InteropServices;

namespace EpubMerge.Gui.WinUI;

/// <summary>Minimal ITaskbarList3 adapter without runtime-generated COM marshalling.</summary>
internal sealed unsafe partial class TaskbarProgress : IDisposable
{
    private nint _instance;
    private readonly nint _window;

    public TaskbarProgress(nint window)
    {
        _window = window;
        var clsid = new Guid("56FDF344-FD6D-11D0-958A-006097C9A090");
        var iid = new Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF");
        Marshal.ThrowExceptionForHR(CoCreateInstance(in clsid, 0, 1, in iid, out _instance));
        try { Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, int>)Vtable[3])(_instance)); }
        catch { Dispose(); throw; }
    }

    private nint* Vtable => *(nint**)_instance;

    public void Update(MergeProgressState state, double value)
    {
        var flag = state switch { MergeProgressState.Normal => 2u, MergeProgressState.Error => 4u, MergeProgressState.Paused => 8u, _ => 0u };
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, uint, int>)Vtable[10])(_instance, _window, flag));
        if (flag != 0)
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, ulong, ulong, int>)Vtable[9])(
                _instance, _window, (ulong)(Math.Clamp(value, 0, 1) * 1000), 1000));
    }

    public void Dispose()
    {
        if (_instance == 0) return;
        ((delegate* unmanaged[Stdcall]<nint, uint>)Vtable[2])(_instance);
        _instance = 0;
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
}
