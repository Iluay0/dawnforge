using System.Diagnostics;
using System.Runtime.InteropServices;
using Dawnforge.Core;
using ImGuiNET;
using Silk.NET.GLFW;
using Silk.NET.Input;
using MouseButton = Silk.NET.Input.MouseButton;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;

namespace Dawnforge.Gui;

internal sealed unsafe class GuiApp
{
    private static readonly Vector2D<int> LogicalSize = new(1120, 780);
    private static readonly Vector2D<int> LogicalMinSize = new(760, 560);
    private const int WaylandAppId = 0x00026001;
    private const string AppId = "dawnforge";

    private readonly IWindow _window;
    private GL _gl = null!;
    private IInputContext _input = null!;
    private ImGuiController? _controller;
    private MainView _view = null!;
    private FileDialogs _dialogs = null!;
    private UiDispatcher _ui = null!;
    private float _rasterScale;
    private float _uiScale;
    private int _activeFrames = 3;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastFrame;
    private const double MinFrameInterval = 0.006;

    public static int Run()
    {
        GlfwWindowing.Use();
        var app = new GuiApp();
        app.Loop();
        return 0;
    }

    private GuiApp()
    {
        var options = WindowOptions.Default with
        {
            Title = "Dawnforge",
            Size = LogicalSize,
            VSync = false,
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3)),
            ShouldSwapAutomatically = true,
        };
        _window = Window.Create(options);
    }

    private Glfw Glfw => GlfwProvider.GLFW.Value;

    private WindowHandle* Handle => (WindowHandle*)_window.Handle;

    private void Loop()
    {
        Glfw.WindowHintString(WaylandAppId, AppId);
        Glfw.WindowHintString((int)WindowHintString.X11ClassName, AppId);
        Glfw.WindowHintString((int)WindowHintString.X11InstanceName, AppId);
        _window.Initialize();
        Glfw.SwapInterval(0);
        _gl = GL.GetApi(_window);
        _input = _window.CreateInput();
        _dialogs = new FileDialogs(() => Glfw.PostEmptyEvent());
        _ui = new UiDispatcher(() => Glfw.PostEmptyEvent());

        var configExists = File.Exists(AppPaths.ConfigFile);
        _view = new MainView(DawnforgeConfig.Load(), configExists, _dialogs, _ui);

        FitToContentScale();
        HookWakeEvents();
        CreateController();

        while (!_window.IsClosing)
        {
            if (_activeFrames > 0 && !IsHidden())
            {
                Glfw.PollEvents();
            }
            else
            {
                Glfw.WaitEventsTimeout(ImGui.GetIO().WantTextInput ? 0.1 : 1.0);
            }
            if (_window.IsClosing)
            {
                break;
            }
            if (_dialogs.Pump() | _ui.Pump())
            {
                _activeFrames = Math.Max(_activeFrames, 3);
            }
            _window.DoUpdate();
            if (IsHidden())
            {
                continue;
            }
            var wait = MinFrameInterval - (_clock.Elapsed.TotalSeconds - _lastFrame);
            if (wait > 0)
            {
                Thread.Sleep(TimeSpan.FromSeconds(wait));
            }
            Frame();
            if (_activeFrames > 0)
            {
                _activeFrames--;
            }
        }

        _view.Dispose();
        _controller?.Dispose();
        _input.Dispose();
        _window.Reset();
        _window.Dispose();
    }

    private bool IsHidden()
    {
        if (Glfw.GetWindowAttrib(Handle, WindowAttributeGetter.Iconified) || !Glfw.GetWindowAttrib(Handle, WindowAttributeGetter.Visible))
        {
            return true;
        }
        var fb = _window.FramebufferSize;
        return fb.X <= 0 || fb.Y <= 0;
    }

    private void Frame()
    {
        var (raster, ui) = Scales();
        if (MathF.Abs(raster - _rasterScale) > 0.01f || MathF.Abs(ui - _uiScale) > 0.01f)
        {
            CreateController();
        }

        var now = _clock.Elapsed.TotalSeconds;
        var dt = (float)Math.Clamp(now - _lastFrame, 1e-4, 0.25);
        _lastFrame = now;

        var fb = _window.FramebufferSize;
        _gl.Viewport(0, 0, (uint)fb.X, (uint)fb.Y);
        _gl.ClearColor(Theme.Win.X, Theme.Win.Y, Theme.Win.Z, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        _controller!.Update(dt);
        _view.Draw();
        ApplyCursor();
        _controller.Render();
        _window.GLContext!.SwapBuffers();
    }

    private void CreateController()
    {
        _controller?.Dispose();
        (_rasterScale, _uiScale) = Scales();
        _controller = new ImGuiController(_gl, _window, _input, () =>
        {
            var io = ImGui.GetIO();
            io.NativePtr->IniFilename = null;
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
            Fonts.Load(_rasterScale);
            io.FontGlobalScale = _uiScale / _rasterScale;
            Widgets.Scale = _uiScale;
            Theme.Apply(_uiScale);
        });
        _activeFrames = Math.Max(_activeFrames, 3);
    }

    private (float Raster, float Ui) Scales()
    {
        var content = 1f;
        if (GlfwExtra.WindowContentScale != null)
        {
            float sx, sy;
            GlfwExtra.WindowContentScale(Handle, &sx, &sy);
            content = sx;
        }
        if (content <= 0)
        {
            content = 1f;
        }
        var size = _window.Size;
        var fb = _window.FramebufferSize;
        var fbScale = size.X > 0 ? (float)fb.X / size.X : 1f;
        if (fbScale <= 0)
        {
            fbScale = 1f;
        }
        var raster = MathF.Max(content, fbScale);
        return (raster, raster / fbScale);
    }

    private void FitToContentScale()
    {
        var (_, ui) = Scales();
        var min = new Vector2D<int>((int)(LogicalMinSize.X * ui), (int)(LogicalMinSize.Y * ui));
        Glfw.SetWindowSizeLimits(Handle, min.X, min.Y, Glfw.DontCare, Glfw.DontCare);
        if (ui > 1.01f)
        {
            _window.Size = new Vector2D<int>((int)(LogicalSize.X * ui), (int)(LogicalSize.Y * ui));
        }
        if (!IsWayland())
        {
            _window.Center();
        }
    }

    private static bool IsWayland() => GlfwExtra.GetPlatform != null && GlfwExtra.GetPlatform() == 0x00060003;

    private static class GlfwExtra
    {
        public static readonly delegate* unmanaged[Cdecl]<WindowHandle*, float*, float*, void> WindowContentScale;
        public static readonly delegate* unmanaged[Cdecl]<int> GetPlatform;

        static GlfwExtra()
        {
            var name = OperatingSystem.IsWindows() ? "glfw3.dll" : OperatingSystem.IsMacOS() ? "libglfw.3.dylib" : "libglfw.so.3";
            if (!NativeLibrary.TryLoad(name, typeof(GuiApp).Assembly, null, out var lib))
            {
                return;
            }
            if (NativeLibrary.TryGetExport(lib, "glfwGetWindowContentScale", out var scale))
            {
                WindowContentScale = (delegate* unmanaged[Cdecl]<WindowHandle*, float*, float*, void>)scale;
            }
            if (NativeLibrary.TryGetExport(lib, "glfwGetPlatform", out var platform))
            {
                GetPlatform = (delegate* unmanaged[Cdecl]<int>)platform;
            }
        }
    }

    private void HookWakeEvents()
    {
        void Wake() => _activeFrames = Math.Max(_activeFrames, 3);
        foreach (var mouse in _input.Mice)
        {
            mouse.MouseMove += (_, _) => Wake();
            mouse.MouseDown += (_, button) =>
            {
                QueueMouseButton(button, true);
                Wake();
            };
            mouse.MouseUp += (_, button) =>
            {
                QueueMouseButton(button, false);
                Wake();
            };
            mouse.Scroll += (_, _) => Wake();
        }
        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += (_, key, _) =>
            {
                QueueModifier(key, true);
                Wake();
            };
            keyboard.KeyUp += (_, key, _) =>
            {
                QueueModifier(key, false);
                Wake();
            };
            keyboard.KeyChar += (_, _) => Wake();
        }
        _window.Resize += _ => Wake();
        _window.FramebufferResize += _ => Wake();
        _window.FocusChanged += _ => Wake();
        _window.StateChanged += _ => Wake();
        _window.FileDrop += paths =>
        {
            _view.AddFiles(paths);
            Wake();
        };
    }

    private static void QueueMouseButton(MouseButton button, bool down)
    {
        var index = button switch
        {
            MouseButton.Left => 0,
            MouseButton.Right => 1,
            MouseButton.Middle => 2,
            _ => -1,
        };
        if (index >= 0 && ImGui.GetCurrentContext() != IntPtr.Zero)
        {
            ImGui.GetIO().AddMouseButtonEvent(index, down);
        }
    }

    private static void QueueModifier(Key key, bool down)
    {
        var mod = key switch
        {
            Key.ControlLeft or Key.ControlRight => ImGuiKey.ModCtrl,
            Key.ShiftLeft or Key.ShiftRight => ImGuiKey.ModShift,
            Key.AltLeft or Key.AltRight => ImGuiKey.ModAlt,
            Key.SuperLeft or Key.SuperRight => ImGuiKey.ModSuper,
            _ => ImGuiKey.None,
        };
        if (mod != ImGuiKey.None && ImGui.GetCurrentContext() != IntPtr.Zero)
        {
            ImGui.GetIO().AddKeyEvent(mod, down);
        }
    }

    private void ApplyCursor()
    {
        if (_input.Mice.Count == 0)
        {
            return;
        }
        var cursor = _input.Mice[0].Cursor;
        var wanted = ImGui.GetMouseCursor() switch
        {
            ImGuiMouseCursor.Hand => StandardCursor.Hand,
            ImGuiMouseCursor.TextInput => StandardCursor.IBeam,
            _ => StandardCursor.Default,
        };
        if (cursor.StandardCursor != wanted)
        {
            cursor.StandardCursor = wanted;
        }
    }
}
