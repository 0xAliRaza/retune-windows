using System.Collections.Concurrent;

namespace Retune.Mpv.Tests.Support;

/// <summary>A single dedicated thread that runs posted work in order, standing in for the WPF dispatcher.</summary>
internal sealed class UiThread : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public UiThread()
    {
        _thread = new Thread(() =>
        {
            foreach (var action in _queue.GetConsumingEnumerable())
            {
                action();
            }
        })
        { IsBackground = true, Name = "test-ui" };
        _thread.Start();
    }

    public void Post(Action action)
    {
        if (!_queue.IsAddingCompleted)
        {
            _queue.Add(action);
        }
    }

    public T Invoke<T>(Func<T> work)
    {
        if (Thread.CurrentThread == _thread)
        {
            return work();
        }

        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try
            {
                done.SetResult(work());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });
        return done.Task.GetAwaiter().GetResult();
    }

    public void Invoke(Action work) => Invoke(() =>
    {
        work();
        return true;
    });

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(5));
        _queue.Dispose();
    }
}
