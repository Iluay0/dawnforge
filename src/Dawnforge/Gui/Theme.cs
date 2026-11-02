using System.Numerics;
using ImGuiNET;

namespace Dawnforge.Gui;

internal static class Theme
{
    public static readonly Vector4 Win = Rgb(0x1b1d21);
    public static readonly Vector4 Panel = Rgb(0x222529);
    public static readonly Vector4 Field = Rgb(0x2a2d33);
    public static readonly Vector4 Hover = Rgb(0x30343b);
    public static readonly Vector4 Border = Rgb(0x35393f);
    public static readonly Vector4 Fg = Rgb(0xe6e7ea);
    public static readonly Vector4 Muted = Rgb(0x8d919b);
    public static readonly Vector4 Accent = Rgb(0xe3a95a);
    public static readonly Vector4 AccentFg = Rgb(0x1b1408);
    public static readonly Vector4 AccentSoft = Rgb(0xe3a95a, 0.14f);
    public static readonly Vector4 Ok = Rgb(0x6cc38a);
    public static readonly Vector4 Err = Rgb(0xe47272);
    public static readonly Vector4 Warn = Rgb(0xe3c25a);
    public static readonly Vector4 Info = Rgb(0x7fb0e6);

    public static Vector4 Rgb(uint hex, float alpha = 1f) =>
        new(((hex >> 16) & 0xff) / 255f, ((hex >> 8) & 0xff) / 255f, (hex & 0xff) / 255f, alpha);

    public static Vector4 WithAlpha(Vector4 c, float alpha) => new(c.X, c.Y, c.Z, c.W * alpha);

    public static Vector4 Brighten(Vector4 c, float factor) =>
        new(MathF.Min(c.X * factor, 1f), MathF.Min(c.Y * factor, 1f), MathF.Min(c.Z * factor, 1f), c.W);

    public static uint U32(Vector4 c) =>
        ((uint)(Math.Clamp(c.W, 0f, 1f) * 255f + 0.5f) << 24)
        | ((uint)(Math.Clamp(c.Z, 0f, 1f) * 255f + 0.5f) << 16)
        | ((uint)(Math.Clamp(c.Y, 0f, 1f) * 255f + 0.5f) << 8)
        | (uint)(Math.Clamp(c.X, 0f, 1f) * 255f + 0.5f);

    public static void Apply(float scale)
    {
        var style = ImGui.GetStyle();
        style.WindowPadding = new Vector2(22, 18);
        style.WindowRounding = 0;
        style.WindowBorderSize = 0;
        style.ChildRounding = 0;
        style.ChildBorderSize = 0;
        style.PopupRounding = 7;
        style.PopupBorderSize = 1;
        style.FrameRounding = 7;
        style.FrameBorderSize = 0;
        style.FramePadding = new Vector2(10, 7);
        style.ItemSpacing = new Vector2(10, 8);
        style.ItemInnerSpacing = new Vector2(7, 6);
        style.ScrollbarSize = 10;
        style.ScrollbarRounding = 6;
        style.GrabRounding = 6;
        style.WindowMinSize = new Vector2(1, 1);
        style.ScaleAllSizes(scale);

        var colors = style.Colors;
        colors[(int)ImGuiCol.Text] = Fg;
        colors[(int)ImGuiCol.TextDisabled] = Muted;
        colors[(int)ImGuiCol.WindowBg] = Win;
        colors[(int)ImGuiCol.ChildBg] = Vector4.Zero;
        colors[(int)ImGuiCol.PopupBg] = Field;
        colors[(int)ImGuiCol.Border] = Border;
        colors[(int)ImGuiCol.BorderShadow] = Vector4.Zero;
        colors[(int)ImGuiCol.FrameBg] = Field;
        colors[(int)ImGuiCol.FrameBgHovered] = Hover;
        colors[(int)ImGuiCol.FrameBgActive] = Hover;
        colors[(int)ImGuiCol.ScrollbarBg] = Vector4.Zero;
        colors[(int)ImGuiCol.ScrollbarGrab] = Border;
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = Muted;
        colors[(int)ImGuiCol.ScrollbarGrabActive] = Muted;
        colors[(int)ImGuiCol.Button] = Panel;
        colors[(int)ImGuiCol.ButtonHovered] = Hover;
        colors[(int)ImGuiCol.ButtonActive] = Hover;
        colors[(int)ImGuiCol.Header] = Panel;
        colors[(int)ImGuiCol.HeaderHovered] = Hover;
        colors[(int)ImGuiCol.HeaderActive] = Hover;
        colors[(int)ImGuiCol.Separator] = Border;
        colors[(int)ImGuiCol.TextSelectedBg] = WithAlpha(Accent, 0.35f);
        colors[(int)ImGuiCol.NavHighlight] = Accent;
        colors[(int)ImGuiCol.CheckMark] = Accent;
        colors[(int)ImGuiCol.SliderGrab] = Accent;
        colors[(int)ImGuiCol.SliderGrabActive] = Accent;
        colors[(int)ImGuiCol.PlotHistogram] = Accent;
        colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(10 / 255f, 11 / 255f, 13 / 255f, 0.6f);
    }
}
