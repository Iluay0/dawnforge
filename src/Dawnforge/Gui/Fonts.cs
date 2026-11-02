using System.Reflection;
using System.Runtime.InteropServices;
using ImGuiNET;

namespace Dawnforge.Gui;

internal static class Icons
{
    public const string Folder = "";
    public const string Upload = "";
    public const string Play = "";
    public const string Broom = "";
    public const string X = "";
    public const string ChevronRight = "";
    public const string ChevronDown = "";
    public const string Warning = "";
    public const string Check = "";
    public const string AlertCircle = "";

    public static readonly ushort[] Codepoints = [0xe0d7, 0xe19e, 0xe13c, 0xe666, 0xe1b2, 0xe06f, 0xe06d, 0xe193, 0xe06c, 0xe077];
}

internal static unsafe class Fonts
{
    public static ImFontPtr Body;
    public static ImFontPtr Medium;
    public static ImFontPtr Label;
    public static ImFontPtr Small;
    public static ImFontPtr Status;
    public static ImFontPtr Caption;
    public static ImFontPtr Header;
    public static ImFontPtr Title;
    public static ImFontPtr Mono;
    public static ImFontPtr MonoSmall;
    public static ImFontPtr IconLarge;

    private static readonly Dictionary<string, (IntPtr Data, int Length)> Data = new();
    private static IntPtr _textRanges;
    private static IntPtr _iconRanges;
    private static string? _cjkPath;
    private static bool _cjkSearched;

    public static void Load(float rasterScale)
    {
        var fonts = ImGui.GetIO().Fonts;
        fonts.Clear();
        EnsureRanges();

        Body = Add("Inter-Regular.ttf", 14f, rasterScale, 16f);
        Medium = Add("Inter-Medium.ttf", 14f, rasterScale, 16f, true);
        Label = Add("Inter-Medium.ttf", 13f, rasterScale, 15f);
        Small = Add("Inter-Regular.ttf", 13f, rasterScale, 15f, true);
        Status = Add("Inter-Regular.ttf", 12.5f, rasterScale, 14f, true);
        Caption = Add("Inter-Medium.ttf", 11.5f, rasterScale, null);
        Header = Add("Inter-SemiBold.ttf", 13f, rasterScale, 16f);
        Title = Add("Inter-SemiBold.ttf", 15.5f, rasterScale, null);
        Mono = Add("JetBrainsMono-Regular.ttf", 12f, rasterScale, null, true);
        MonoSmall = Add("JetBrainsMono-Regular.ttf", 11f, rasterScale, null, true);
        IconLarge = AddIconsOnly(30f, rasterScale);

        ImGui.GetIO().NativePtr->FontDefault = Body.NativePtr;
        ImGui.GetIO().FontGlobalScale = 1f;
    }

    private static ImFontPtr Add(string file, float size, float scale, float? iconSize, bool cjk = false)
    {
        var fonts = ImGui.GetIO().Fonts;
        var (data, length) = Resource(file);
        var cfg = new ImFontConfigPtr(ImGuiNative.ImFontConfig_ImFontConfig());
        cfg.FontDataOwnedByAtlas = false;
        cfg.OversampleH = 2;
        cfg.OversampleV = 1;
        var font = fonts.AddFontFromMemoryTTF(data, length, size * scale, cfg, _textRanges);
        cfg.Destroy();

        if (iconSize is { } px)
        {
            var (iconData, iconLength) = Resource("Lucide.ttf");
            var icfg = new ImFontConfigPtr(ImGuiNative.ImFontConfig_ImFontConfig());
            icfg.FontDataOwnedByAtlas = false;
            icfg.MergeMode = true;
            icfg.PixelSnapH = true;
            icfg.GlyphMinAdvanceX = px * scale;
            icfg.GlyphOffset = new System.Numerics.Vector2(0, (size - px) * 0.5f * scale + size * 0.3f * scale);
            fonts.AddFontFromMemoryTTF(iconData, iconLength, px * scale, icfg, _iconRanges);
            icfg.Destroy();
        }
        if (cjk)
        {
            MergeCjk(size, scale);
        }
        return font;
    }

    private static void MergeCjk(float size, float scale)
    {
        var path = CjkFontPath();
        if (path is null)
        {
            return;
        }
        (IntPtr Data, int Length) font;
        try
        {
            font = LoadFile(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _cjkPath = null;
            return;
        }
        var fonts = ImGui.GetIO().Fonts;
        var cfg = new ImFontConfigPtr(ImGuiNative.ImFontConfig_ImFontConfig());
        cfg.FontDataOwnedByAtlas = false;
        cfg.MergeMode = true;
        cfg.OversampleH = 1;
        cfg.OversampleV = 1;
        fonts.AddFontFromMemoryTTF(font.Data, font.Length, size * scale, cfg, fonts.GetGlyphRangesJapanese());
        cfg.Destroy();
    }

    private static string? CjkFontPath()
    {
        if (_cjkSearched)
        {
            return _cjkPath;
        }
        _cjkSearched = true;
        _cjkPath = OperatingSystem.IsWindows() ? FindWindowsCjk() : FindLinuxCjk();
        return _cjkPath;
    }

    private static string? FindWindowsCjk()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
        foreach (var name in new[] { "YuGothR.ttc", "YuGothM.ttc", "meiryo.ttc", "msgothic.ttc" })
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    private static string? FindLinuxCjk()
    {
        string[] names =
        [
            "NotoSansCJK-Regular.ttc",
            "NotoSansCJKjp-Regular.otf",
            "NotoSansJP-Regular.otf",
            "NotoSansJP-Regular.ttf",
        ];
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome))
        {
            dataHome = Path.Combine(home, ".local", "share");
        }
        string[] roots =
        [
            Path.Combine(dataHome, "fonts"),
            Path.Combine(home, ".fonts"),
            "/usr/local/share/fonts",
            "/usr/share/fonts",
        ];

        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "Noto*", options))
                {
                    found.TryAdd(Path.GetFileName(file), file);
                }
            }
            catch (IOException)
            {
            }
        }
        foreach (var name in names)
        {
            if (found.TryGetValue(name, out var path))
            {
                return path;
            }
        }
        return null;
    }

    private static (IntPtr, int) LoadFile(string path)
    {
        if (Data.TryGetValue(path, out var cached))
        {
            return cached;
        }
        var bytes = File.ReadAllBytes(path);
        var ptr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        Data[path] = (ptr, bytes.Length);
        return (ptr, bytes.Length);
    }

    private static ImFontPtr AddIconsOnly(float size, float scale)
    {
        var fonts = ImGui.GetIO().Fonts;
        var (data, length) = Resource("Lucide.ttf");
        var cfg = new ImFontConfigPtr(ImGuiNative.ImFontConfig_ImFontConfig());
        cfg.FontDataOwnedByAtlas = false;
        cfg.PixelSnapH = true;
        var font = fonts.AddFontFromMemoryTTF(data, length, size * scale, cfg, _iconRanges);
        cfg.Destroy();
        return font;
    }

    private static (IntPtr, int) Resource(string file)
    {
        if (Data.TryGetValue(file, out var cached))
        {
            return cached;
        }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Fonts." + file)
            ?? throw new InvalidOperationException($"Missing embedded font {file}");
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        var ptr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        Data[file] = (ptr, bytes.Length);
        return (ptr, bytes.Length);
    }

    private static void EnsureRanges()
    {
        if (_textRanges != IntPtr.Zero)
        {
            return;
        }
        ushort[] text = [0x0020, 0x00ff, 0x0100, 0x017f, 0x2010, 0x2027, 0x2190, 0x2193, 0];
        _textRanges = Pin(text);

        var icons = new List<ushort>();
        foreach (var cp in Icons.Codepoints)
        {
            icons.Add(cp);
            icons.Add(cp);
        }
        icons.Add(0);
        _iconRanges = Pin(icons.ToArray());
    }

    private static IntPtr Pin(ushort[] values)
    {
        var ptr = Marshal.AllocHGlobal(values.Length * sizeof(ushort));
        for (var i = 0; i < values.Length; i++)
        {
            Marshal.WriteInt16(ptr, i * sizeof(ushort), (short)values[i]);
        }
        return ptr;
    }
}
