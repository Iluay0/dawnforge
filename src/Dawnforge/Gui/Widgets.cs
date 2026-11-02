using System.Numerics;
using ImGuiNET;

namespace Dawnforge.Gui;

internal enum ButtonKind
{
    Normal,
    Primary,
    Danger,
}

internal static class Widgets
{
    public static float Scale { get; set; } = 1f;

    public static float S(float v) => v * Scale;

    public static float Size(ImFontPtr font) => font.FontSize * ImGui.GetIO().FontGlobalScale;

    public static Vector2 Measure(ImFontPtr font, string text) =>
        font.CalcTextSizeA(Size(font), float.MaxValue, 0f, text);

    public static void Text(ImDrawListPtr dl, ImFontPtr font, Vector2 pos, Vector4 color, string text) =>
        dl.AddText(font, Size(font), pos, Theme.U32(color), text);

    public static void TextCentredY(ImDrawListPtr dl, ImFontPtr font, float x, float centreY, Vector4 color, string text) =>
        Text(dl, font, new Vector2(x, MathF.Round(centreY - Size(font) * 0.5f)), color, text);

    public static float Tracked(ImDrawListPtr dl, ImFontPtr font, Vector2 pos, Vector4 color, string text, float em = 0.06f)
    {
        var x = pos.X;
        var extra = Size(font) * em;
        foreach (var ch in text)
        {
            var s = ch.ToString();
            Text(dl, font, new Vector2(x, pos.Y), color, s);
            x += Measure(font, s).X + extra;
        }
        return x - extra - pos.X;
    }

    public static float TrackedWidth(ImFontPtr font, string text, float em = 0.06f)
    {
        var w = 0f;
        foreach (var ch in text)
        {
            w += Measure(font, ch.ToString()).X;
        }
        return w + Size(font) * em * Math.Max(0, text.Length - 1);
    }

    public static string Ellipsize(ImFontPtr font, string text, float maxWidth)
    {
        if (Measure(font, text).X <= maxWidth)
        {
            return text;
        }
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (Measure(font, text[..mid] + "…").X <= maxWidth)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return lo == 0 ? "…" : text[..lo].TrimEnd() + "…";
    }

    public static float ButtonWidth(ImFontPtr font, string? icon, string label, float padX = 14f)
    {
        var w = S(padX) * 2 + Measure(font, label).X;
        if (icon != null)
        {
            w += Measure(font, icon).X + S(7);
        }
        return w;
    }

    public static bool Button(string id, ImFontPtr font, string? icon, string label, Vector2 pos, float height,
        ButtonKind kind = ButtonKind.Normal, bool enabled = true, float padX = 14f)
    {
        var width = ButtonWidth(font, icon, label, padX);
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton(id, new Vector2(width, height)) && enabled;
        var hovered = enabled && ImGui.IsItemHovered();
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        var dl = ImGui.GetWindowDrawList();
        var min = pos;
        var max = pos + new Vector2(width, height);
        var alpha = enabled ? 1f : 0.45f;
        Vector4 bg, fg, border;
        if (kind == ButtonKind.Primary)
        {
            bg = hovered ? Theme.Brighten(Theme.Accent, 1.06f) : Theme.Accent;
            fg = Theme.AccentFg;
            border = Vector4.Zero;
        }
        else
        {
            bg = hovered ? Theme.Hover : Theme.Panel;
            fg = kind == ButtonKind.Danger ? Theme.Err : Theme.Fg;
            border = Theme.Border;
        }
        var r = S(7);
        dl.AddRectFilled(min, max, Theme.U32(Theme.WithAlpha(bg, alpha)), r);
        if (border.W > 0)
        {
            dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Theme.U32(Theme.WithAlpha(border, alpha)), r, ImDrawFlags.None, 1f);
        }
        var x = min.X + S(padX);
        var cy = min.Y + height * 0.5f;
        if (icon != null)
        {
            TextCentredY(dl, font, x, cy, Theme.WithAlpha(fg, alpha), icon);
            x += Measure(font, icon).X + S(7);
        }
        TextCentredY(dl, font, x, cy, Theme.WithAlpha(fg, alpha), label);
        return clicked;
    }

    public readonly record struct Run(string Text, ImFontPtr Font, Vector4 Color);

    public static float WrapRuns(ImDrawListPtr dl, Vector2 pos, float width, float lineHeight, Run[] runs, bool draw)
    {
        var x = 0f;
        var y = 0f;
        foreach (var run in runs)
        {
            var start = 0;
            while (start < run.Text.Length)
            {
                var end = start;
                while (end < run.Text.Length && run.Text[end] != ' ')
                {
                    end++;
                }
                if (end < run.Text.Length)
                {
                    end++;
                }
                var word = run.Text[start..end];
                var w = Measure(run.Font, word.TrimEnd()).X;
                if (x > 0 && x + w > width)
                {
                    x = 0;
                    y += lineHeight;
                }
                if (draw)
                {
                    TextCentredY(dl, run.Font, pos.X + x, pos.Y + y + lineHeight * 0.5f, run.Color, word);
                }
                x += Measure(run.Font, word).X;
                start = end;
            }
        }
        return y + lineHeight;
    }

    public static void Tooltip(string text)
    {
        ImGui.PushFont(Fonts.Small);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(S(10), S(7)));
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(text);
        ImGui.EndTooltip();
        ImGui.PopStyleVar();
        ImGui.PopFont();
    }

    public static void DashedRect(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color, float rounding, float thickness, float dash, float gap)
    {
        var points = RoundedRectPath(min, max, rounding);
        var total = 0f;
        for (var i = 0; i < points.Count; i++)
        {
            total += Vector2.Distance(points[i], points[(i + 1) % points.Count]);
        }
        var period = dash + gap;
        var count = Math.Max(1, (int)MathF.Round(total / period));
        var scale = total / (count * period);
        dash *= scale;
        gap *= scale;

        var drawing = true;
        var remaining = dash;
        var current = new List<Vector2> { points[0] };
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            var segment = Vector2.Distance(a, b);
            var t = 0f;
            while (segment - t > remaining)
            {
                t += remaining;
                var p = Vector2.Lerp(a, b, t / segment);
                if (drawing)
                {
                    current.Add(p);
                    Stroke(dl, current, color, thickness);
                    current.Clear();
                }
                else
                {
                    current.Add(p);
                }
                drawing = !drawing;
                remaining = drawing ? dash : gap;
            }
            remaining -= segment - t;
            if (drawing)
            {
                current.Add(b);
            }
        }
        if (drawing && current.Count > 1)
        {
            Stroke(dl, current, color, thickness);
        }
    }

    private static void Stroke(ImDrawListPtr dl, List<Vector2> points, uint color, float thickness)
    {
        for (var i = 0; i + 1 < points.Count; i++)
        {
            dl.AddLine(points[i], points[i + 1], color, thickness);
        }
    }

    private static List<Vector2> RoundedRectPath(Vector2 min, Vector2 max, float r)
    {
        var points = new List<Vector2>();
        const int steps = 8;
        void Arc(Vector2 c, float start)
        {
            for (var i = 0; i <= steps; i++)
            {
                var a = start + MathF.PI * 0.5f * i / steps;
                points.Add(c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r);
            }
        }
        Arc(new Vector2(min.X + r, min.Y + r), MathF.PI);
        Arc(new Vector2(max.X - r, min.Y + r), MathF.PI * 1.5f);
        Arc(new Vector2(max.X - r, max.Y - r), 0f);
        Arc(new Vector2(min.X + r, max.Y - r), MathF.PI * 0.5f);
        return points;
    }
}
