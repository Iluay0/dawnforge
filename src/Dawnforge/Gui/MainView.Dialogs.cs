using System.Numerics;
using Dawnforge.Core;
using ImGuiNET;
using static Dawnforge.Gui.Widgets;

namespace Dawnforge.Gui;

internal sealed partial class MainView
{
    private const string FirstRunId = "##firstrun";
    private const string ConflictId = "##conflict";

    private bool _firstRunOpen;
    private bool _firstRunPending;
    private string _firstRunPath = "";
    private bool _firstRunValid;
    private List<Conflict>? _conflicts;
    private bool _conflictsPending;
    private long _lastRecheck;

    private bool ModalOpen => _firstRunOpen || _conflicts != null;

    private void OpenFirstRun()
    {
        if (_firstRunOpen)
        {
            return;
        }
        _firstRunOpen = true;
        _firstRunPending = true;
        _firstRunPath = _gamePath;
        _firstRunValid = GamePath.TryResolve(_firstRunPath, out _, out _);
    }

    private void OpenConflicts(List<Conflict> conflicts)
    {
        _conflicts = conflicts;
        _conflictsPending = true;
    }

    private void RecheckGamePath()
    {
        var now = Environment.TickCount64;
        if (now - _lastRecheck < 2000 || _running || _firstRunOpen)
        {
            return;
        }
        _lastRecheck = now;
        var wasValid = _gameValid;
        ValidateGame();
        if (wasValid && !_gameValid)
        {
            _log.Warn(_gameError);
        }
        if (!_gameValid)
        {
            OpenFirstRun();
        }
    }

    private void DrawModals()
    {
        if (_firstRunOpen)
        {
            if (_firstRunPending)
            {
                ImGui.OpenPopup(FirstRunId);
                _firstRunPending = false;
            }
            DrawFirstRun();
        }
        else if (_conflicts != null)
        {
            if (_conflictsPending)
            {
                ImGui.OpenPopup(ConflictId);
                _conflictsPending = false;
            }
            DrawConflicts(_conflicts);
        }
    }

    private bool BeginDialog(string id, float contentHeight, out Vector2 origin, out float width)
    {
        var vp = ImGui.GetMainViewport();
        var padX = S(22);
        var padY = S(20);
        var w = MathF.Min(S(520), vp.WorkSize.X - S(48));
        var h = contentHeight + padY * 2;
        ImGui.SetNextWindowPos(vp.WorkPos + (vp.WorkSize - new Vector2(w, h)) * 0.5f);
        ImGui.SetNextWindowSize(new Vector2(w, h));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(padX, padY));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, S(12));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Win);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.Border);
        var open = ImGui.BeginPopupModal(id, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(3);
        origin = open ? ImGui.GetCursorScreenPos() : Vector2.Zero;
        width = w - padX * 2;
        return open;
    }

    private static float TitleHeight() => Size(Fonts.Title) * 1.45f;

    private static float ParagraphLineHeight() => Size(Fonts.Small) * 1.45f;

    private static Run[] FirstRunText() =>
    [
        new("Dawnforge needs the game files to upgrade mods. Pick the folder that contains ", Fonts.Small, Theme.Muted),
        new("game/ ", Fonts.MonoSmall, Theme.Muted),
        new("and ", Fonts.Small, Theme.Muted),
        new("boot/", Fonts.MonoSmall, Theme.Muted),
        new(". This is asked once and saved in your user config folder.", Fonts.Small, Theme.Muted),
    ];

    private void DrawFirstRun()
    {
        var gap = S(14);
        var fieldH = S(34);
        var w = MathF.Min(S(520), ImGui.GetMainViewport().WorkSize.X - S(48)) - S(44);
        var dlMeasure = ImGui.GetForegroundDrawList();
        var paraH = WrapRuns(dlMeasure, Vector2.Zero, w, ParagraphLineHeight(), FirstRunText(), false);
        var contentH = TitleHeight() + gap + paraH + gap + fieldH + gap + S(34);

        if (!BeginDialog(FirstRunId, contentH, out var origin, out var width))
        {
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        var y = origin.Y;
        TextCentredY(dl, Fonts.Title, origin.X, y + TitleHeight() * 0.5f, Theme.Fg, "Where is Final Fantasy XIV installed?");
        y += TitleHeight() + gap;
        WrapRuns(dl, new Vector2(origin.X, y), width, ParagraphLineHeight(), FirstRunText(), true);
        y += paraH + gap;

        var buttonW = ButtonWidth(Fonts.Medium, Icons.Folder, "Browse");
        var fieldMin = new Vector2(origin.X, y);
        var fieldMax = new Vector2(origin.X + width - buttonW - S(12), y + fieldH);
        var cy = y + fieldH * 0.5f;
        dl.AddRectFilled(fieldMin, fieldMax, Theme.U32(Theme.Field), S(7));

        var textX = fieldMin.X + S(10);
        ImGui.PushFont(Fonts.Mono);
        var frameH = Size(Fonts.Mono) + S(8);
        ImGui.SetCursorScreenPos(new Vector2(textX, cy - frameH * 0.5f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0, S(4)));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Vector4.Zero);
        ImGui.SetNextItemWidth(fieldMax.X - S(10) - textX);
        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetKeyboardFocusHere();
        }
        if (ImGui.InputText("##firstpath", ref _firstRunPath, 4096))
        {
            _firstRunValid = GamePath.TryResolve(_firstRunPath, out _, out _);
        }
        var active = ImGui.IsItemActive();
        var submitted = ImGui.IsItemFocused() && ImGui.IsKeyPressed(ImGuiKey.Enter);
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();
        ImGui.PopFont();
        if (_firstRunPath.Length == 0)
        {
            TextCentredY(dl, Fonts.Mono, textX, cy, Theme.Muted, OperatingSystem.IsWindows()
                ? "C:\\path\\to\\FINAL FANTASY XIV - A Realm Reborn"
                : "/path/to/FINAL FANTASY XIV - A Realm Reborn");
        }
        var border = active ? Theme.Accent : _firstRunPath.Trim().Length > 0 && !_firstRunValid ? Theme.Err : Theme.Border;
        dl.AddRect(fieldMin + new Vector2(0.5f), fieldMax - new Vector2(0.5f), Theme.U32(border), S(7), ImDrawFlags.None, 1f);
        if (_firstRunPath.Trim().Length > 0 && !_firstRunValid && ImGui.IsMouseHoveringRect(fieldMin, fieldMax))
        {
            GamePath.TryResolve(_firstRunPath, out _, out var error);
            Tooltip(error);
        }

        if (Button("##firstbrowse", Fonts.Medium, Icons.Folder, "Browse", new Vector2(fieldMax.X + S(12), y), fieldH,
                enabled: !_dialogs.Busy))
        {
            _dialogs.PickFolder(_firstRunPath, path =>
            {
                _firstRunPath = path;
                _firstRunValid = GamePath.TryResolve(path, out _, out _);
            }, e => _log.Err($"folder dialog failed: {e}"));
        }
        y += fieldH + gap;

        var okW = ButtonWidth(Fonts.Medium, null, "Continue");
        var clicked = Button("##firstok", Fonts.Medium, null, "Continue", new Vector2(origin.X + width - okW, y), S(34),
            ButtonKind.Primary, _firstRunValid);
        if ((clicked || submitted) && _firstRunValid)
        {
            _gamePath = _firstRunPath.Trim();
            ValidateGame();
            _firstRunOpen = false;
            _lastRecheck = Environment.TickCount64;
            if (SaveConfig())
            {
                _log.Info($"game path saved to {Tilde(AppPaths.ConfigFile)}");
            }
            _log.Ok($"game found at {Tilde(_gamePath)}");
            StartService();
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private void DrawConflicts(List<Conflict> conflicts)
    {
        const string text = "Nothing is written over without asking. Choose what happens to these items.";
        var gap = S(14);
        var w = MathF.Min(S(520), ImGui.GetMainViewport().WorkSize.X - S(48)) - S(44);
        var paraRuns = new Run[] { new(text, Fonts.Small, Theme.Muted) };
        var paraH = WrapRuns(ImGui.GetForegroundDrawList(), Vector2.Zero, w, ParagraphLineHeight(), paraRuns, false);
        var rowH = Size(Fonts.Small) * 1.45f + S(14) + 2;
        var rowGap = S(6);
        var listFull = conflicts.Count * rowH + Math.Max(0, conflicts.Count - 1) * rowGap;
        var listH = MathF.Min(S(200), listFull);
        var contentH = TitleHeight() + gap + paraH + gap + listH + gap + S(34);

        if (!BeginDialog(ConflictId, contentH, out var origin, out var width))
        {
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        var y = origin.Y;
        TextCentredY(dl, Fonts.Title, origin.X, y + TitleHeight() * 0.5f, Theme.Fg, "Some output names are already in use");
        y += TitleHeight() + gap;
        WrapRuns(dl, new Vector2(origin.X, y), width, ParagraphLineHeight(), paraRuns, true);
        y += paraH + gap;

        ImGui.SetCursorScreenPos(new Vector2(origin.X, y));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.BeginChild("##conflist", new Vector2(width, listH), ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        ImGui.PopStyleVar();
        var cdl = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var rw = ImGui.GetContentRegionAvail().X;
        var ry = start.Y;
        foreach (var c in conflicts)
        {
            var min = new Vector2(start.X, ry);
            var max = new Vector2(start.X + rw, ry + rowH);
            cdl.AddRectFilled(min, max, Theme.U32(Theme.Panel), S(7));
            cdl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Theme.U32(Theme.Border), S(7), ImDrawFlags.None, 1f);
            var rcy = ry + rowH * 0.5f;
            var reasonW = Measure(Fonts.Status, c.Reason).X;
            var nameMax = rw - S(20) - reasonW - S(12);
            TextCentredY(cdl, Fonts.Small, min.X + S(10), rcy, Theme.Fg, Ellipsize(Fonts.Small, c.Item.OutputName.Trim() + ".pmp", nameMax));
            TextCentredY(cdl, Fonts.Status, max.X - S(10) - reasonW, rcy, Theme.Muted, c.Reason);
            ry += rowH + rowGap;
        }
        ImGui.Dummy(new Vector2(rw, listFull));
        ImGui.EndChild();
        y += listH + gap;

        var overW = ButtonWidth(Fonts.Medium, null, "Overwrite");
        var skipW = ButtonWidth(Fonts.Medium, null, "Skip these");
        var cancelW = ButtonWidth(Fonts.Medium, null, "Cancel");
        var right = origin.X + width;
        var overwrite = Button("##confover", Fonts.Medium, null, "Overwrite", new Vector2(right - overW, y), S(34), ButtonKind.Danger);
        var skip = Button("##confskip", Fonts.Medium, null, "Skip these", new Vector2(right - overW - S(8) - skipW, y), S(34));
        var cancel = Button("##confcancel", Fonts.Medium, null, "Cancel",
            new Vector2(right - overW - skipW - cancelW - S(16), y), S(34));

        if (cancel || overwrite || skip)
        {
            _conflicts = null;
            ImGui.CloseCurrentPopup();
        }
        if (cancel)
        {
            _log.Info("conversion cancelled");
        }
        else if (skip)
        {
            SkipConflicts(conflicts);
            RunQueue(false, conflicts.Count);
        }
        else if (overwrite)
        {
            foreach (var c in conflicts)
            {
                _log.Warn($"will overwrite {c.Item.OutputName.Trim()}.pmp");
            }
            RunQueue(true);
        }
        ImGui.EndPopup();
    }
}
