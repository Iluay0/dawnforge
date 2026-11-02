using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Dawnforge.Core;

namespace Dawnforge.Gui;

internal sealed class FileDialogs(Action wake)
{
    private readonly ConcurrentQueue<Action> _completed = new();
    private volatile bool _busy;

    public bool Busy => _busy;

    public void OpenModpacks(string? directory, Action<string[]> onPicked, Action<string> onError)
    {
        var spec = string.Join(",", SupportedFormats.Extensions.Select(e => e.TrimStart('.')));
        Run(() => Nfd.OpenMultiple("Modpacks", spec, ExistingDirectory(directory)), paths =>
        {
            if (paths.Length > 0)
            {
                onPicked(paths);
            }
        }, onError);
    }

    public void PickFolder(string? directory, Action<string> onPicked, Action<string> onError)
    {
        Run(() => Nfd.PickFolder(ExistingDirectory(directory)), path =>
        {
            if (!string.IsNullOrEmpty(path))
            {
                onPicked(path);
            }
        }, onError);
    }

    public bool Pump()
    {
        var any = false;
        while (_completed.TryDequeue(out var action))
        {
            action();
            any = true;
        }
        return any;
    }

    private static string? ExistingDirectory(string? directory) =>
        !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) ? directory : null;

    private void Run<T>(Func<T> show, Action<T> onDone, Action<string> onError)
    {
        if (_busy)
        {
            return;
        }
        _busy = true;
        var thread = new Thread(() =>
        {
            Action result;
            try
            {
                var value = show();
                result = () => onDone(value);
            }
            catch (Exception e)
            {
                result = () => onError(e.Message);
            }
            _completed.Enqueue(() =>
            {
                _busy = false;
                result();
            });
            wake();
        })
        {
            IsBackground = true,
            Name = "file dialog",
        };
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }
        thread.Start();
    }

    private static class Nfd
    {
        private const string Lib = "nfd";

        private enum Result
        {
            Error,
            Okay,
            Cancel,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FilterItem
        {
            public IntPtr Name;
            public IntPtr Spec;
        }

        [DllImport(Lib)] private static extern Result NFD_Init();
        [DllImport(Lib)] private static extern void NFD_Quit();
        [DllImport(Lib)] private static extern IntPtr NFD_GetError();
        [DllImport(Lib)] private static extern void NFD_ClearError();
        [DllImport(Lib)] private static extern void NFD_FreePathU8(IntPtr path);
        [DllImport(Lib)] private static extern Result NFD_PickFolderU8(out IntPtr outPath, IntPtr defaultPath);
        [DllImport(Lib)] private static extern Result NFD_OpenDialogMultipleU8(out IntPtr outPaths, FilterItem[] filters, uint count, IntPtr defaultPath);
        [DllImport(Lib)] private static extern Result NFD_PathSet_GetCount(IntPtr pathSet, out uint count);
        [DllImport(Lib)] private static extern Result NFD_PathSet_GetPathU8(IntPtr pathSet, uint index, out IntPtr outPath);
        [DllImport(Lib)] private static extern void NFD_PathSet_FreePathU8(IntPtr path);
        [DllImport(Lib)] private static extern void NFD_PathSet_Free(IntPtr pathSet);

        public static string? PickFolder(string? defaultPath)
        {
            var def = Utf8(defaultPath);
            Init();
            try
            {
                var result = NFD_PickFolderU8(out var outPath, def);
                Check(result);
                if (result != Result.Okay || outPath == IntPtr.Zero)
                {
                    return null;
                }
                var path = Marshal.PtrToStringUTF8(outPath);
                NFD_FreePathU8(outPath);
                return path;
            }
            finally
            {
                NFD_Quit();
                Marshal.FreeCoTaskMem(def);
            }
        }

        public static string[] OpenMultiple(string filterName, string filterSpec, string? defaultPath)
        {
            var name = Utf8(filterName);
            var spec = Utf8(filterSpec);
            var def = Utf8(defaultPath);
            Init();
            try
            {
                var result = NFD_OpenDialogMultipleU8(out var set, [new FilterItem { Name = name, Spec = spec }], 1, def);
                Check(result);
                if (result != Result.Okay || set == IntPtr.Zero)
                {
                    return [];
                }
                try
                {
                    Check(NFD_PathSet_GetCount(set, out var count));
                    var paths = new List<string>((int)count);
                    for (uint i = 0; i < count; i++)
                    {
                        Check(NFD_PathSet_GetPathU8(set, i, out var item));
                        var path = Marshal.PtrToStringUTF8(item);
                        NFD_PathSet_FreePathU8(item);
                        if (!string.IsNullOrEmpty(path))
                        {
                            paths.Add(path);
                        }
                    }
                    return paths.ToArray();
                }
                finally
                {
                    NFD_PathSet_Free(set);
                }
            }
            finally
            {
                NFD_Quit();
                Marshal.FreeCoTaskMem(name);
                Marshal.FreeCoTaskMem(spec);
                Marshal.FreeCoTaskMem(def);
            }
        }

        private static void Init()
        {
            if (NFD_Init() == Result.Error)
            {
                throw new InvalidOperationException(LastError());
            }
        }

        private static void Check(Result result)
        {
            if (result == Result.Error)
            {
                throw new InvalidOperationException(LastError());
            }
        }

        private static string LastError()
        {
            var message = Marshal.PtrToStringUTF8(NFD_GetError()) ?? "unknown file dialog error";
            NFD_ClearError();
            return message;
        }

        private static IntPtr Utf8(string? value) => value == null ? IntPtr.Zero : Marshal.StringToCoTaskMemUTF8(value);
    }
}
