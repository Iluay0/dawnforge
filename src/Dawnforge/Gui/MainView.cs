using System.Numerics;
using Dawnforge.Core;
using ImGuiNET;
using static Dawnforge.Gui.Widgets;

namespace Dawnforge.Gui;

internal sealed partial class MainView : IDisposable
{
    private readonly ModpackQueue _queue = new();
    private readonly LogBook _log = new();
    private readonly FileDialogs _dialogs;
    private readonly UiDispatcher _ui;
    private readonly DawnforgeConfig _config;
    private string _gamePath;
    private string _outputFolder;
    private bool _gameValid;
    private string _gameError = "";
    private bool _logOpen = true;
    private int _logSeen;
    private bool _logStick = true;

    public MainView(DawnforgeConfig config, bool configExists, FileDialogs dialogs, UiDispatcher ui)
    {
        _dialogs = dialogs;
        _ui = ui;
        _config = config;
        _gamePath = config.GamePath ?? "";
        _outputFolder = config.OutputFolder ?? "";
        ValidateGame();

        var version = typeof(MainView).Assembly.GetName().Version?.ToString(3) ?? "";
        _log.Info(configExists
            ? $"Dawnforge {version} · config loaded from {Tilde(AppPaths.ConfigFile)}"
            : $"Dawnforge {version} · no config yet ({Tilde(AppPaths.ConfigFile)})");
        if (_gamePath.Length == 0)
        {
            _log.Warn("game path not set");
        }
        else if (_gameValid)
        {
            _log.Info($"game found at {Tilde(_gamePath)}");
        }
        else
        {
            _log.Warn(_gameError);
        }

        if (_gameValid)
        {
            StartService();
        }
        else
        {
            OpenFirstRun();
        }
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        if (ModalOpen)
        {
            return;
        }
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!SupportedFormats.IsSupported(path))
            {
                _log.Warn($"skipped {name}: not a modpack (.ttmp, .ttmp2, .pmp)");
                continue;
            }
            var item = _queue.TryAdd(path);
            if (item == null)
            {
                _log.Warn($"skipped {name}: file not found");
                continue;
            }
            _log.Info($"queued {item.SourceName} ({item.FormatLabel})");
        }
    }

    public void Draw()
    {
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(vp.WorkPos);
        ImGui.SetNextWindowSize(vp.WorkSize);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(S(22), S(18)));
        ImGui.Begin("##dawnforge", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoScrollWithMouse
            | ImGuiWindowFlags.NoNavFocus);
        ImGui.PopStyleVar();

        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var bottom = ImGui.GetWindowPos().Y + ImGui.GetWindowSize().Y - S(20);

        var y = origin.Y;
        y = DrawPathRow(origin.X, y, width, "Game path", "##game", _gamePath, true, BrowseGame);
        y += S(10);
        y = DrawPathRow(origin.X, y, width, "Output folder", "##out", _outputFolder, false, BrowseOutput);
        y += S(18);
        y = DrawDropZone(origin.X, y, width);
        y += S(18);

        var logHeight = LogHeight();
        var logTop = bottom - logHeight;
        DrawQueue(origin.X, y, width, logTop - S(18));
        DrawLog(origin.X, logTop, width, logHeight);

        ImGui.End();

        RecheckGamePath();
        DrawModals();
    }

    private float DrawPathRow(float x, float y, float width, string label, string id, string value, bool isGame, Action browse)
    {
        var h = S(34);
        var dl = ImGui.GetWindowDrawList();
        var cy = y + h * 0.5f;
        TextCentredY(dl, Fonts.Label, x, cy, Theme.Muted, label);

        var buttonWidth = ButtonWidth(Fonts.Medium, Icons.Folder, "Browse");
        var fieldMin = new Vector2(x + S(112) + S(12), y);
        var fieldMax = new Vector2(x + width - buttonWidth - S(12), y + h);
        dl.AddRectFilled(fieldMin, fieldMax, Theme.U32(Theme.Field), S(7));

        var textX = fieldMin.X + S(10);
        if (isGame)
        {
            var dotColor = _gameValid ? Theme.Ok : Theme.Err;
            dl.AddCircleFilled(new Vector2(textX + S(4), cy), S(4), Theme.U32(dotColor));
            textX += S(8) + S(8);
        }

        var display = value;
        ImGui.PushFont(Fonts.Mono);
        var frameH = Size(Fonts.Mono) + S(8);
        ImGui.SetCursorScreenPos(new Vector2(textX, cy - frameH * 0.5f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0, S(4)));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Vector4.Zero);
        ImGui.SetNextItemWidth(fieldMax.X - S(10) - textX);
        ImGui.InputText(id, ref display, 4096, ImGuiInputTextFlags.ReadOnly);
        var active = ImGui.IsItemActive();
        var hovered = ImGui.IsMouseHoveringRect(fieldMin, fieldMax);
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();
        ImGui.PopFont();
        if (value.Length == 0 && !active)
        {
            TextCentredY(dl, Fonts.Mono, textX, cy, Theme.Muted, "Not set");
        }

        dl.AddRect(fieldMin + new Vector2(0.5f), fieldMax - new Vector2(0.5f),
            Theme.U32(active ? Theme.Accent : Theme.Border), S(7), ImDrawFlags.None, 1f);

        if (hovered && isGame && !active && !ModalOpen)
        {
            Tooltip(_gameValid ? "FFXIV install found" : _gameError);
        }

        var browseEnabled = !_dialogs.Busy && !_running && !(isGame && _serviceState == ServiceState.Loading);
        if (Button(id + "browse", Fonts.Medium, Icons.Folder, "Browse", new Vector2(fieldMax.X + S(12), y), h,
                enabled: browseEnabled))
        {
            browse();
        }
        return y + h;
    }

    private float DrawDropZone(float x, float y, float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var iconH = Size(Fonts.IconLarge);
        var titleH = Size(Fonts.Title) * 1.45f;
        var subH = Size(Fonts.Small) * 1.45f;
        var buttonH = S(30);
        var content = iconH + S(10) + titleH + S(4) + subH + S(12) + buttonH;
        var h = MathF.Max(S(150), content + S(44));
        var min = new Vector2(x, y);
        var max = new Vector2(x + width, y + h);

        ImGui.SetCursorScreenPos(min);
        ImGui.SetNextItemAllowOverlap();
        var zoneClicked = ImGui.InvisibleButton("##drop", max - min);
        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenOverlappedByItem) && !_dialogs.Busy;
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        dl.AddRectFilled(min, max, Theme.U32(Theme.Panel), S(10));
        if (hovered)
        {
            dl.AddRectFilled(min, max, Theme.U32(Theme.AccentSoft), S(10));
        }
        var inset = S(0.75f);
        DashedRect(dl, min + new Vector2(inset), max - new Vector2(inset), Theme.U32(hovered ? Theme.Accent : Theme.Border),
            S(10), S(1.5f), S(6), S(4));

        var cx = x + width * 0.5f;
        var cy = y + (h - content) * 0.5f;
        var iconW = Measure(Fonts.IconLarge, Icons.Upload).X;
        Text(dl, Fonts.IconLarge, new Vector2(MathF.Round(cx - iconW * 0.5f), cy), Theme.Accent, Icons.Upload);
        cy += iconH + S(10);

        const string title = "Drop modpacks here";
        var titleW = Measure(Fonts.Title, title).X;
        TextCentredY(dl, Fonts.Title, MathF.Round(cx - titleW * 0.5f), cy + titleH * 0.5f, Theme.Fg, title);
        cy += titleH + S(4);

        const string sub = ".ttmp, .ttmp2 and .pmp · every output is a .pmp · inputs are never modified";
        var subText = Ellipsize(Fonts.Small, sub, width - S(44));
        var subW = Measure(Fonts.Small, subText).X;
        TextCentredY(dl, Fonts.Small, MathF.Round(cx - subW * 0.5f), cy + subH * 0.5f, Theme.Muted, subText);
        cy += subH + S(12);

        var bw = ButtonWidth(Fonts.Label, Icons.Folder, "Browse files");
        var buttonClicked = Button("##browsefiles", Fonts.Label, Icons.Folder, "Browse files",
            new Vector2(MathF.Round(cx - bw * 0.5f), cy), buttonH, enabled: !_dialogs.Busy);

        if ((zoneClicked || buttonClicked) && !_dialogs.Busy)
        {
            BrowseFiles();
        }
        return y + h;
    }

    private void DrawQueue(float x, float y, float width, float bottom)
    {
        var dl = ImGui.GetWindowDrawList();
        var headH = S(34);
        var cy = y + headH * 0.5f;
        var headY = MathF.Round(cy - Size(Fonts.Header) * 0.5f);
        var qw = Tracked(dl, Fonts.Header, new Vector2(x, headY), Theme.Muted, "QUEUE");
        Text(dl, Fonts.Header, new Vector2(x + qw + Measure(Fonts.Header, " ").X + S(1), headY), Theme.Fg,
            _queue.Items.Count.ToString());

        var convertW = ButtonWidth(Fonts.Medium, Icons.Play, "Convert all");
        var clearW = ButtonWidth(Fonts.Medium, Icons.Broom, "Clear finished");
        var right = x + width;
        if (Button("##convert", Fonts.Medium, Icons.Play, "Convert all", new Vector2(right - convertW, y), headH,
                ButtonKind.Primary, _queue.HasQueued && !_running && _serviceState == ServiceState.Ready))
        {
            ConvertAll();
        }
        if (Button("##clear", Fonts.Medium, Icons.Broom, "Clear finished", new Vector2(right - convertW - S(10) - clearW, y), headH,
                enabled: _queue.HasFinished && !_running))
        {
            var n = _queue.ClearFinished();
            _log.Info($"cleared {n} finished item{(n == 1 ? "" : "s")}");
        }

        y += headH + S(8);
        var cols = Columns(width);
        var colY = y;
        var colX = x + S(12);
        string[] labels = ["SOURCE", "FORMAT", "OUTPUT NAME", "STATUS"];
        for (var i = 0; i < labels.Length; i++)
        {
            Tracked(dl, Fonts.Caption, new Vector2(colX + cols[i].Start, colY), Theme.Muted, labels[i]);
        }
        y += Size(Fonts.Caption) * 1.45f + S(8);

        var listH = MathF.Max(bottom - y, S(60));
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.BeginChild("##rows", new Vector2(width, listH), ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        ImGui.PopStyleVar();
        DrawRows();
        ImGui.EndChild();
    }

    private readonly record struct Column(float Start, float Width);

    private static Column[] Columns(float width)
    {
        var gap = S(12);
        var inner = width - S(24) - gap * 4 - S(72) - S(32);
        var src = inner * 1.5f / 3.75f;
        var outW = inner * 1.25f / 3.75f;
        var stat = inner - src - outW;
        var c0 = new Column(0, src);
        var c1 = new Column(c0.Start + src + gap, S(72));
        var c2 = new Column(c1.Start + S(72) + gap, outW);
        var c3 = new Column(c2.Start + outW + gap, stat);
        var c4 = new Column(c3.Start + stat + gap, S(32));
        return [c0, c1, c2, c3, c4];
    }

    private void DrawRows()
    {
        var dl = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;

        if (_queue.Items.Count == 0)
        {
            const string empty = "Nothing queued. Drop modpacks above or browse for them.";
            var h = Size(Fonts.Small) * 1.45f + S(28);
            var min = start + new Vector2(0.5f);
            var max = start + new Vector2(width, h) - new Vector2(0.5f);
            DashedRect(dl, min, max, Theme.U32(Theme.Border), S(7), 1f, S(4), S(3));
            var text = Ellipsize(Fonts.Small, empty, width - S(24));
            var tw = Measure(Fonts.Small, text).X;
            TextCentredY(dl, Fonts.Small, MathF.Round(start.X + (width - tw) * 0.5f), start.Y + h * 0.5f, Theme.Muted, text);
            ImGui.Dummy(new Vector2(width, h));
            return;
        }

        var cols = Columns(width);
        var rowH = S(56);
        QueueItem? remove = null;
        var y = start.Y;
        foreach (var item in _queue.Items)
        {
            if (DrawRow(dl, item, new Vector2(start.X, y), width, rowH, cols))
            {
                remove = item;
            }
            y += rowH + S(6);
        }
        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(width, y - start.Y - S(6)));

        if (remove != null)
        {
            _queue.Remove(remove);
            _log.Info($"removed {remove.SourceName}");
        }
    }

    private bool DrawRow(ImDrawListPtr dl, QueueItem item, Vector2 pos, float width, float h, Column[] cols)
    {
        var min = pos;
        var max = pos + new Vector2(width, h);
        var border = item.Status switch
        {
            QueueStatus.Done => Theme.WithAlpha(Theme.Ok, 0.35f),
            QueueStatus.Failed => Theme.WithAlpha(Theme.Err, 0.4f),
            _ => Theme.Border,
        };
        dl.AddRectFilled(min, max, Theme.U32(Theme.Panel), S(7));
        dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Theme.U32(border), S(7), ImDrawFlags.None, 1f);

        var x0 = pos.X + S(12);
        var cy = pos.Y + h * 0.5f;
        ImGui.PushID(item.Id);

        var nameH = Size(Fonts.Medium) * 1.45f;
        var dirH = Size(Fonts.MonoSmall) * 1.45f;
        var srcTop = cy - (nameH + S(1) + dirH) * 0.5f;
        var c = cols[0];
        TextCentredY(dl, Fonts.Medium, x0 + c.Start, srcTop + nameH * 0.5f, Theme.Fg, Ellipsize(Fonts.Medium, item.SourceName, c.Width));
        TextCentredY(dl, Fonts.MonoSmall, x0 + c.Start, srcTop + nameH + S(1) + dirH * 0.5f, Theme.Muted,
            Ellipsize(Fonts.MonoSmall, Tilde(item.SourceDirectory), c.Width));
        if (ImGui.IsMouseHoveringRect(new Vector2(x0 + c.Start, srcTop), new Vector2(x0 + c.Start + c.Width, srcTop + nameH + dirH))
            && ImGui.IsWindowHovered())
        {
            Tooltip(item.SourcePath);
        }

        c = cols[1];
        var chipW = Measure(Fonts.MonoSmall, item.FormatLabel).X + S(16);
        var chipMin = new Vector2(x0 + c.Start, cy - S(11));
        var chipMax = chipMin + new Vector2(chipW, S(22));
        dl.AddRectFilled(chipMin, chipMax, Theme.U32(Theme.Field), S(11));
        dl.AddRect(chipMin + new Vector2(0.5f), chipMax - new Vector2(0.5f), Theme.U32(Theme.Border), S(11), ImDrawFlags.None, 1f);
        TextCentredY(dl, Fonts.MonoSmall, chipMin.X + S(8), cy, Theme.Muted, item.FormatLabel);

        DrawOutputName(dl, item, x0 + cols[2].Start, cy, cols[2].Width);
        DrawStatus(dl, item, x0 + cols[3].Start, cy, cols[3].Width);

        c = cols[4];
        var rmMin = new Vector2(x0 + c.Start + S(1), cy - S(15));
        var canRemove = item.Status != QueueStatus.Converting;
        ImGui.SetCursorScreenPos(rmMin);
        var clicked = ImGui.InvisibleButton("##rm", new Vector2(S(30), S(30))) && canRemove;
        var hovered = canRemove && ImGui.IsItemHovered();
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            dl.AddRectFilled(rmMin, rmMin + new Vector2(S(30)), Theme.U32(Theme.Hover), S(6));
            Tooltip("Remove");
        }
        var iconColor = hovered ? Theme.Err : Theme.WithAlpha(Theme.Muted, canRemove ? 1f : 0.3f);
        var iconW = Measure(Fonts.Medium, Icons.X).X;
        TextCentredY(dl, Fonts.Medium, MathF.Round(rmMin.X + (S(30) - iconW) * 0.5f), cy, iconColor, Icons.X);

        ImGui.PopID();
        return clicked;
    }

    private void DrawOutputName(ImDrawListPtr dl, QueueItem item, float x, float cy, float width)
    {
        var issue = _queue.IssueFor(item, _outputFolder);
        var h = S(30);
        var extW = Measure(Fonts.MonoSmall, ".pmp").X + S(18);
        var warnW = issue != NameIssue.None ? Measure(Fonts.Medium, Icons.Warning).X + S(6) : 0f;
        var fieldMin = new Vector2(x, cy - h * 0.5f);
        var fieldMax = new Vector2(x + width - extW - warnW, cy + h * 0.5f);
        var extMax = new Vector2(fieldMax.X + extW, fieldMax.Y);
        var editable = item.Status == QueueStatus.Queued && !item.InRun;

        dl.AddRectFilled(fieldMin, extMax, Theme.U32(Theme.Field), S(7));

        var name = item.OutputName;
        ImGui.PushFont(Fonts.Small);
        var frameH = Size(Fonts.Small) + S(8);
        ImGui.SetCursorScreenPos(new Vector2(fieldMin.X + S(10), cy - frameH * 0.5f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0, S(4)));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Vector4.Zero);
        ImGui.SetNextItemWidth(MathF.Max(fieldMax.X - fieldMin.X - S(20), S(10)));
        ImGui.BeginDisabled(!editable);
        if (ImGui.InputText("##name", ref name, 255))
        {
            item.OutputName = name;
        }
        var active = ImGui.IsItemActive();
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            if (string.IsNullOrWhiteSpace(item.OutputName))
            {
                item.OutputName = item.DefaultOutputName;
            }
            _log.Info($"{item.SourceName} → {item.OutputName}.pmp");
        }
        ImGui.EndDisabled();
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();
        ImGui.PopFont();

        var borderColor = issue != NameIssue.None ? Theme.Warn : active ? Theme.Accent : Theme.Border;
        dl.AddLine(new Vector2(fieldMax.X, fieldMin.Y + 1), new Vector2(fieldMax.X, fieldMax.Y - 1), Theme.U32(Theme.Border));
        dl.AddRect(new Vector2(fieldMax.X, fieldMin.Y) + new Vector2(0, 0.5f), extMax - new Vector2(0.5f),
            Theme.U32(Theme.Border), S(7), ImDrawFlags.RoundCornersRight, 1f);
        dl.AddRect(fieldMin + new Vector2(0.5f), new Vector2(fieldMax.X + 0.5f, fieldMax.Y - 0.5f),
            Theme.U32(borderColor), S(7), ImDrawFlags.RoundCornersLeft, 1f);
        TextCentredY(dl, Fonts.MonoSmall, fieldMax.X + S(9), cy, Theme.Muted, ".pmp");

        if (issue != NameIssue.None)
        {
            var wx = extMax.X + S(6);
            TextCentredY(dl, Fonts.Medium, wx, cy, Theme.Warn, Icons.Warning);
            var iw = Measure(Fonts.Medium, Icons.Warning).X;
            if (ImGui.IsMouseHoveringRect(new Vector2(wx, cy - S(10)), new Vector2(wx + iw, cy + S(10))) && ImGui.IsWindowHovered())
            {
                Tooltip(issue switch
                {
                    NameIssue.InvalidName => "Name can't be empty or contain \\ / : * ? \" < > |",
                    NameIssue.DuplicateInQueue => "Another queued item has this name",
                    _ => "A file with this name exists in the output folder",
                });
            }
        }
    }

    private static void DrawStatus(ImDrawListPtr dl, QueueItem item, float x, float cy, float width)
    {
        var lineH = Size(Fonts.Status) * 1.45f;
        var converting = item.Status == QueueStatus.Converting;
        var total = converting ? lineH + S(5) + S(4) : lineH;
        var top = cy - total * 0.5f;
        var lineCy = top + lineH * 0.5f;

        var (dot, text) = item.Status switch
        {
            QueueStatus.Converting => (Theme.Accent, Theme.Fg),
            QueueStatus.Done => (Theme.Ok, Theme.Ok),
            QueueStatus.Failed => (Theme.Err, Theme.Err),
            QueueStatus.Skipped => (Theme.Warn, Theme.Muted),
            _ => (Theme.Muted, Theme.Fg),
        };
        var dotC = new Vector2(x + S(3.5f), lineCy);
        if (converting)
        {
            dl.AddCircleFilled(dotC, S(6.5f), Theme.U32(Theme.AccentSoft));
        }
        dl.AddCircleFilled(dotC, S(3.5f), Theme.U32(dot));
        var tx = x + S(7) + S(7);
        TextCentredY(dl, Fonts.Status, tx, lineCy, text, Ellipsize(Fonts.Status, item.Message, width - (tx - x)));

        if (converting)
        {
            var barMin = new Vector2(x, top + lineH + S(5));
            var barMax = new Vector2(x + width, barMin.Y + S(4));
            dl.AddRectFilled(barMin, barMax, Theme.U32(Theme.Field), S(2));
            var fill = Math.Clamp(item.Progress, 0f, 1f);
            if (fill > 0)
            {
                dl.AddRectFilled(barMin, new Vector2(barMin.X + (barMax.X - barMin.X) * fill, barMax.Y), Theme.U32(Theme.Accent), S(2));
            }
        }
    }

    private float LogHeaderHeight() => S(9) * 2 + Size(Fonts.Header) * 1.45f;

    private float LogBodyHeight()
    {
        var lineH = Size(Fonts.Mono) * 1.6f;
        var content = S(16) + Math.Max(_log.Count, 1) * lineH;
        return MathF.Min(S(190), content);
    }

    private float LogHeight() => LogHeaderHeight() + (_logOpen ? 1f + LogBodyHeight() : 0f);

    private void DrawLog(float x, float y, float width, float height)
    {
        var dl = ImGui.GetWindowDrawList();
        var min = new Vector2(x, y);
        var max = new Vector2(x + width, y + height);
        dl.AddRectFilled(min, max, Theme.U32(Theme.Panel), S(10));

        var headH = LogHeaderHeight();
        ImGui.SetCursorScreenPos(min);
        if (ImGui.InvisibleButton("##logtoggle", new Vector2(width, headH)))
        {
            _logOpen = !_logOpen;
        }
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        var color = hovered ? Theme.Fg : Theme.Muted;
        var cy = y + headH * 0.5f;
        var hx = x + S(12);
        var chevron = _logOpen ? Icons.ChevronDown : Icons.ChevronRight;
        TextCentredY(dl, Fonts.Header, hx, cy, color, chevron);
        hx += Measure(Fonts.Header, chevron).X + S(8);
        Tracked(dl, Fonts.Header, new Vector2(hx, MathF.Round(cy - Size(Fonts.Header) * 0.5f)), color, "LOG");

        var count = _log.Count;
        if (count > 0)
        {
            var n = $"{count} line{(count == 1 ? "" : "s")}";
            var nw = Measure(Fonts.Label, n).X;
            TextCentredY(dl, Fonts.Label, x + width - S(12) - nw, cy, color, n);
        }

        if (_logOpen)
        {
            var bodyTop = y + headH;
            dl.AddLine(new Vector2(x, bodyTop + 0.5f), new Vector2(x + width, bodyTop + 0.5f), Theme.U32(Theme.Border));
            ImGui.SetCursorScreenPos(new Vector2(x, bodyTop + 1));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(S(12), S(8)));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(S(10), Size(Fonts.Mono) * 0.6f));
            ImGui.BeginChild("##logbody", new Vector2(width, height - headH - 1), ImGuiChildFlags.AlwaysUseWindowPadding,
                ImGuiWindowFlags.NoBackground);
            DrawLogLines();
            ImGui.EndChild();
            ImGui.PopStyleVar(2);
        }

        dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Theme.U32(Theme.Border), S(10), ImDrawFlags.None, 1f);
    }

    private void DrawLogLines()
    {
        var entries = _log.Snapshot();
        ImGui.PushFont(Fonts.Mono);
        var levelX = S(64) + S(10);
        var msgX = levelX + S(44) + S(10);
        foreach (var e in entries)
        {
            var baseX = ImGui.GetCursorPosX();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Muted);
            ImGui.TextUnformatted(e.Time.ToString("HH:mm:ss"));
            ImGui.PopStyleColor();
            ImGui.SameLine(baseX + levelX);
            ImGui.PushStyleColor(ImGuiCol.Text, e.Level switch
            {
                LogLevel.Ok => Theme.Ok,
                LogLevel.Warn => Theme.Warn,
                LogLevel.Err => Theme.Err,
                _ => Theme.Info,
            });
            ImGui.TextUnformatted(e.Level switch
            {
                LogLevel.Ok => "OK",
                LogLevel.Warn => "WARN",
                LogLevel.Err => "ERR",
                _ => "INFO",
            });
            ImGui.PopStyleColor();
            ImGui.SameLine(baseX + msgX);
            ImGui.PushTextWrapPos(0f);
            ImGui.TextUnformatted(e.Message);
            ImGui.PopTextWrapPos();
        }
        ImGui.PopFont();
        if (entries.Length != _logSeen && _logStick)
        {
            ImGui.SetScrollHereY(1f);
        }
        else
        {
            _logStick = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 1f;
        }
        _logSeen = entries.Length;
    }

    private void BrowseFiles()
    {
        _dialogs.OpenModpacks(null, AddFiles, e => _log.Err($"file dialog failed: {e}"));
    }

    private void BrowseGame()
    {
        _dialogs.PickFolder(_gamePath, path =>
        {
            if (!GamePath.TryResolve(path, out _, out var error))
            {
                _log.Warn($"{error}; game path unchanged");
                return;
            }
            var changed = !string.Equals(path, _gamePath, StringComparison.Ordinal) || _serviceState != ServiceState.Ready;
            _gamePath = path;
            ValidateGame();
            _log.Ok($"game found at {Tilde(path)}");
            SaveConfig();
            if (changed)
            {
                StartService();
            }
        }, e => _log.Err($"folder dialog failed: {e}"));
    }

    private void BrowseOutput()
    {
        _dialogs.PickFolder(_outputFolder, path =>
        {
            _outputFolder = path;
            _log.Info($"output folder set to {Tilde(path)}");
            SaveConfig();
        }, e => _log.Err($"folder dialog failed: {e}"));
    }

    private bool SaveConfig()
    {
        _config.GamePath = _gamePath.Length > 0 ? _gamePath : null;
        _config.OutputFolder = _outputFolder.Length > 0 ? _outputFolder : null;
        try
        {
            _config.Save();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Err($"could not save config: {e.Message}");
            return false;
        }
    }

    private void ValidateGame()
    {
        _gameValid = GamePath.TryResolve(_gamePath, out _, out var error);
        _gameError = error;
    }

    private static string Tilde(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return path;
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 1 && (path == home || path.StartsWith(home + "/", StringComparison.Ordinal)))
        {
            return "~" + path[home.Length..];
        }
        return path;
    }
}
