using System.Collections.Concurrent;

namespace Dawnforge.Gui;

internal sealed class UiDispatcher(Action wake)
{
    private readonly ConcurrentQueue<Action> _actions = new();

    public void Post(Action action)
    {
        _actions.Enqueue(action);
        wake();
    }

    public Task<T> Invoke<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });
        return tcs.Task;
    }

    public bool Pump()
    {
        var any = false;
        while (_actions.TryDequeue(out var action))
        {
            action();
            any = true;
        }
        return any;
    }
}
