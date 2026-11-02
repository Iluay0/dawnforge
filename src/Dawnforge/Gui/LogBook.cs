namespace Dawnforge.Gui;

internal enum LogLevel
{
    Info,
    Ok,
    Warn,
    Err,
}

internal readonly record struct LogEntry(DateTime Time, LogLevel Level, string Message);

internal sealed class LogBook
{
    private readonly List<LogEntry> _entries = [];
    private readonly object _lock = new();

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    public void Add(LogLevel level, string message)
    {
        lock (_lock)
        {
            _entries.Add(new LogEntry(DateTime.Now, level, message));
        }
    }

    public void Info(string message) => Add(LogLevel.Info, message);
    public void Ok(string message) => Add(LogLevel.Ok, message);
    public void Warn(string message) => Add(LogLevel.Warn, message);
    public void Err(string message) => Add(LogLevel.Err, message);

    public LogEntry[] Snapshot()
    {
        lock (_lock)
        {
            return _entries.ToArray();
        }
    }
}
