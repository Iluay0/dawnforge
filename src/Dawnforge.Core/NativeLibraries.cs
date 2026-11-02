using System.Reflection;
using System.Runtime.InteropServices;

namespace Dawnforge.Core;

internal static class NativeLibraries
{
    private static int _registered;

    public static void Register()
    {
        if (!OperatingSystem.IsLinux() || Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(typeof(TeximpNet.Surface).Assembly, Resolve);
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name == "libdl.so" && NativeLibrary.TryLoad("libdl.so.2", out var handle))
        {
            return handle;
        }
        return IntPtr.Zero;
    }
}
