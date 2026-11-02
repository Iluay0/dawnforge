using System.Diagnostics;
using Dawnforge.Core;

namespace Dawnforge.Gui;

internal sealed partial class MainView
{
    private enum ServiceState
    {
        None,
        Loading,
        Ready,
        Failed,
    }

    private readonly record struct Conflict(QueueItem Item, string Reason);

    private ModpackUpgrader? _service;
    private ServiceState _serviceState;
    private int _serviceGeneration;
    private bool _running;

    private void StartService()
    {
        var generation = ++_serviceGeneration;
        _service?.Dispose();
        _service = null;
        _serviceState = ServiceState.Loading;
        _log.Info("Loading game data…");

        var path = _gamePath;
        var service = new ModpackUpgrader(line => _ui.Post(() => _log.Info(line)));
        var clock = Stopwatch.StartNew();
        Task.Run(async () =>
        {
            try
            {
                await service.Initialise(path);
                _ui.Post(() =>
                {
                    if (generation != _serviceGeneration)
                    {
                        service.Dispose();
                        return;
                    }
                    _service = service;
                    _serviceState = ServiceState.Ready;
                    _log.Ok($"game data ready ({clock.Elapsed.TotalSeconds:0.0} s)");
                });
            }
            catch (Exception e)
            {
                _ui.Post(() =>
                {
                    service.Dispose();
                    if (generation == _serviceGeneration)
                    {
                        _serviceState = ServiceState.Failed;
                        _log.Err($"could not load game data: {e.Message}");
                    }
                });
            }
        });
    }

    private void ConvertAll()
    {
        ValidateGame();
        if (!_gameValid)
        {
            OpenFirstRun();
            return;
        }
        if (string.IsNullOrWhiteSpace(_outputFolder))
        {
            _log.Warn("output folder not set; choose one with Browse");
            return;
        }

        foreach (var item in _queue.Items.Where(i => i.Status == QueueStatus.Queued && !ModpackQueue.IsValidName(i.OutputName)))
        {
            _log.Warn($"excluded {item.SourceName}: invalid output name \"{item.OutputName}\"");
        }

        var conflicts = FindConflicts();
        if (conflicts.Count > 0)
        {
            OpenConflicts(conflicts);
            return;
        }
        RunQueue(false);
    }

    private List<QueueItem> Convertible() =>
        _queue.Items.Where(i => i.Status == QueueStatus.Queued && ModpackQueue.IsValidName(i.OutputName)).ToList();

    private List<Conflict> FindConflicts()
    {
        var items = Convertible();
        var existing = ExistingOutputs(_outputFolder);
        var conflicts = new List<Conflict>();
        foreach (var item in items)
        {
            var key = item.OutputName.Trim();
            if (existing.Contains(key))
            {
                conflicts.Add(new Conflict(item, "exists in output folder"));
            }
            else if (items.Any(o => o != item && string.Equals(o.OutputName.Trim(), key, StringComparison.OrdinalIgnoreCase)))
            {
                conflicts.Add(new Conflict(item, "also queued under this name"));
            }
        }
        return conflicts;
    }

    private static HashSet<string> ExistingOutputs(string folder)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(folder))
            {
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    if (string.Equals(Path.GetExtension(file), ".pmp", StringComparison.OrdinalIgnoreCase))
                    {
                        names.Add(Path.GetFileNameWithoutExtension(file));
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return names;
    }

    private void SkipConflicts(List<Conflict> conflicts)
    {
        foreach (var c in conflicts)
        {
            c.Item.Status = QueueStatus.Skipped;
            c.Item.Message = "Skipped · name in use";
            _log.Warn($"skipped {c.Item.SourceName}: {c.Item.OutputName.Trim()}.pmp {c.Reason}");
        }
    }

    private void RunQueue(bool overwrite, int skippedBefore = 0)
    {
        var service = _service;
        if (service == null)
        {
            return;
        }
        var items = Convertible();
        if (items.Count == 0)
        {
            _log.Info($"finished: 0 upgraded, 0 failed, {skippedBefore} skipped");
            return;
        }

        var folder = _outputFolder;
        if (!Directory.Exists(folder))
        {
            try
            {
                Directory.CreateDirectory(folder);
                _log.Info($"created output folder {Tilde(folder)}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _log.Err($"could not create output folder: {e.Message}");
                return;
            }
        }

        foreach (var item in items)
        {
            item.InRun = true;
        }
        _running = true;
        var tally = new RunTally { Skipped = skippedBefore };
        Task.Run(() => RunItems(service, items, folder, overwrite, tally));
    }

    private sealed class RunTally
    {
        public int Upgraded;
        public int Failed;
        public int Skipped;
    }

    private async Task RunItems(ModpackUpgrader service, List<QueueItem> items, string folder, bool overwrite, RunTally tally)
    {
        foreach (var item in items)
        {
            var target = await _ui.Invoke(() => BeginItem(item, folder, overwrite, tally));
            if (target == null)
            {
                continue;
            }

            UpgradeResult result;
            try
            {
                result = await service.Upgrade(item.SourcePath, target, new ItemProgress(this, item));
            }
            catch (Exception e)
            {
                result = new UpgradeResult(UpgradeStatus.Failed, e.Message);
            }
            _ui.Post(() => FinishItem(item, target, result, tally));
        }

        _ui.Post(() =>
        {
            foreach (var item in items)
            {
                item.InRun = false;
            }
            _running = false;
            var line = $"finished: {tally.Upgraded} upgraded, {tally.Failed} failed, {tally.Skipped} skipped";
            if (tally.Failed > 0)
            {
                _log.Warn(line);
            }
            else
            {
                _log.Ok(line);
            }
        });
    }

    private string? BeginItem(QueueItem item, string folder, bool overwrite, RunTally tally)
    {
        if (!_queue.Items.Contains(item) || item.Status != QueueStatus.Queued)
        {
            item.InRun = false;
            return null;
        }
        var name = item.OutputName.Trim();
        if (!overwrite && ExistingOutputs(folder).Contains(name))
        {
            item.InRun = false;
            item.Status = QueueStatus.Skipped;
            item.Message = "Skipped · name in use";
            tally.Skipped++;
            _log.Warn($"skipped {item.SourceName}: {name}.pmp exists in output folder");
            return null;
        }
        item.Status = QueueStatus.Converting;
        item.Progress = 0;
        item.Message = "Starting…";
        _log.Info($"upgrading {item.SourceName} → {name}.pmp");
        return Path.Combine(folder, name + ".pmp");
    }

    private void FinishItem(QueueItem item, string target, UpgradeResult result, RunTally tally)
    {
        item.InRun = false;
        item.Progress = 1f;
        var name = Path.GetFileName(target);
        switch (result.Status)
        {
            case UpgradeStatus.Changed:
            case UpgradeStatus.Unchanged:
                item.Status = QueueStatus.Done;
                var size = SizeMb(target);
                item.Message = result.Status == UpgradeStatus.Changed ? $"Done · {size} MB" : "Done · no upgrade needed";
                tally.Upgraded++;
                _log.Ok(result.Status == UpgradeStatus.Changed
                    ? $"wrote {name} ({size} MB)"
                    : $"wrote {name} ({size} MB, no upgrade needed)");
                break;
            case UpgradeStatus.AlreadyDawntrail:
                item.Status = QueueStatus.Skipped;
                item.Message = "Already Dawntrail · nothing written";
                tally.Skipped++;
                _log.Warn($"{item.SourceName}: already Dawntrail, nothing written");
                break;
            default:
                item.Status = QueueStatus.Failed;
                item.Message = result.Message ?? "Failed";
                tally.Failed++;
                _log.Err($"{item.SourceName}: {item.Message}");
                break;
        }
    }

    private static string SizeMb(string path)
    {
        try
        {
            return (new FileInfo(path).Length / 1048576.0).ToString("0.0");
        }
        catch (IOException)
        {
            return "?";
        }
    }

    private sealed class ItemProgress(MainView view, QueueItem item) : IProgress<(int current, int total, string message)>
    {
        public void Report((int current, int total, string message) value) =>
            view._ui.Post(() => view.ApplyProgress(item, value.current, value.total, value.message));
    }

    private void ApplyProgress(QueueItem item, int current, int total, string message)
    {
        if (item.Status != QueueStatus.Converting)
        {
            return;
        }
        var fraction = total > 0 ? Math.Clamp((float)current / total, 0f, 1f) : 0f;
        var (start, span) = message switch
        {
            _ when message.StartsWith("Loading", StringComparison.Ordinal) => (0.02f, 0f),
            _ when message.StartsWith("Upgrading models", StringComparison.Ordinal) => (0.05f, 0.45f),
            _ when message.StartsWith("Upgrading textures", StringComparison.Ordinal) => (0.5f, 0.35f),
            _ when message.StartsWith("Upgrading partial", StringComparison.Ordinal) => (0.85f, 0.07f),
            _ when message.StartsWith("Writing", StringComparison.Ordinal) => (0.93f, 0f),
            _ => (item.Progress, 0f),
        };
        item.Progress = MathF.Max(item.Progress, start + span * fraction);
        item.Message = total > 0 ? $"{message} ({current + 1}/{total})" : message;
    }

    public void Dispose()
    {
        _serviceGeneration++;
        _service?.Dispose();
        _service = null;
    }
}
