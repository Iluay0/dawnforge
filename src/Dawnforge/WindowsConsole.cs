using System.Runtime.InteropServices;

namespace Dawnforge;

internal static partial class WindowsConsole
{
    private const uint AttachParentProcess = 0xFFFFFFFF;
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;

    public static void AttachToParent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        if (IsValid(GetStdHandle(StdOutputHandle)) && IsValid(GetStdHandle(StdErrorHandle)))
        {
            return;
        }
        AttachConsole(AttachParentProcess);
    }

    private static bool IsValid(IntPtr handle) => handle != IntPtr.Zero && handle != new IntPtr(-1);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint processId);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetStdHandle(int handle);
}
