using System.Diagnostics;
using System.Text;

namespace Dawnforge.Core;

public sealed class CallbackTraceListener(Action<string> callback) : TraceListener
{
    private readonly StringBuilder _buffer = new();
    private readonly Lock _lock = new();

    public override void Write(string? message)
    {
        lock (_lock)
        {
            _buffer.Append(message);
        }
    }

    public override void WriteLine(string? message)
    {
        string line;
        lock (_lock)
        {
            _buffer.Append(message);
            line = _buffer.ToString();
            _buffer.Clear();
        }
        foreach (var part in line.Split('\n'))
        {
            var trimmed = part.TrimEnd('\r');
            if (trimmed.Length > 0)
            {
                callback(trimmed);
            }
        }
    }
}
