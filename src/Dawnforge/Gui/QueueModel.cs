using Dawnforge.Core;

namespace Dawnforge.Gui;

internal enum ModpackFormat
{
    Ttmp,
    Ttmp2,
    Pmp,
}

internal enum QueueStatus
{
    Queued,
    Converting,
    Done,
    Failed,
    Skipped,
}

internal sealed class QueueItem
{
    public required int Id { get; init; }
    public required string SourcePath { get; init; }
    public required ModpackFormat Format { get; init; }
    public required string OutputName { get; set; }
    public QueueStatus Status { get; set; } = QueueStatus.Queued;
    public float Progress { get; set; }
    public string Message { get; set; } = "Queued";
    public bool InRun { get; set; }

    public string SourceName => Path.GetFileName(SourcePath);
    public string SourceDirectory => Path.GetDirectoryName(SourcePath) ?? "";
    public string DefaultOutputName => Path.GetFileNameWithoutExtension(SourcePath);

    public string FormatLabel => Format switch
    {
        ModpackFormat.Ttmp => "TTMP",
        ModpackFormat.Ttmp2 => "TTMP2",
        _ => "PMP",
    };
}

internal enum NameIssue
{
    None,
    InvalidName,
    DuplicateInQueue,
    ExistsInOutput,
}

internal sealed class ModpackQueue
{
    private readonly List<QueueItem> _items = [];
    private readonly Dictionary<string, (bool Exists, long Tick)> _existsCache = new(StringComparer.Ordinal);
    private int _nextId = 1;

    public IReadOnlyList<QueueItem> Items => _items;

    public bool HasQueued => _items.Any(i => i.Status == QueueStatus.Queued);
    public bool HasFinished => _items.Any(IsFinished);

    public QueueItem? TryAdd(string path)
    {
        if (!SupportedFormats.IsSupported(path) || !File.Exists(path))
        {
            return null;
        }
        var item = new QueueItem
        {
            Id = _nextId++,
            SourcePath = Path.GetFullPath(path),
            Format = FormatOf(path),
            OutputName = Path.GetFileNameWithoutExtension(path),
        };
        _items.Add(item);
        return item;
    }

    public void Remove(QueueItem item) => _items.Remove(item);

    public int ClearFinished() => _items.RemoveAll(IsFinished);

    public NameIssue IssueFor(QueueItem item, string? outputFolder)
    {
        if (item.Status != QueueStatus.Queued)
        {
            return NameIssue.None;
        }
        if (!IsValidName(item.OutputName))
        {
            return NameIssue.InvalidName;
        }
        var key = item.OutputName.Trim();
        if (_items.Any(o => o != item && o.Status == QueueStatus.Queued
            && string.Equals(o.OutputName.Trim(), key, StringComparison.OrdinalIgnoreCase)))
        {
            return NameIssue.DuplicateInQueue;
        }
        return OutputExists(outputFolder, key) ? NameIssue.ExistsInOutput : NameIssue.None;
    }

    public static bool IsValidName(string name)
    {
        var trimmed = name.Trim();
        return trimmed.Length > 0 && trimmed.All(c => !char.IsControl(c) && !InvalidNameChars.Contains(c));
    }

    public const string InvalidNameChars = "\\/:*?\"<>|";

    private bool OutputExists(string? folder, string name)
    {
        if (string.IsNullOrWhiteSpace(folder) || name.Length == 0)
        {
            return false;
        }
        string path;
        try
        {
            path = Path.Combine(folder, name + ".pmp");
        }
        catch (ArgumentException)
        {
            return false;
        }
        var now = Environment.TickCount64;
        if (_existsCache.TryGetValue(path, out var cached) && now - cached.Tick < 1000)
        {
            return cached.Exists;
        }
        var exists = File.Exists(path);
        _existsCache[path] = (exists, now);
        return exists;
    }

    private static bool IsFinished(QueueItem item) =>
        item.Status is QueueStatus.Done or QueueStatus.Failed or QueueStatus.Skipped;

    private static ModpackFormat FormatOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".ttmp" => ModpackFormat.Ttmp,
        ".ttmp2" => ModpackFormat.Ttmp2,
        _ => ModpackFormat.Pmp,
    };
}
