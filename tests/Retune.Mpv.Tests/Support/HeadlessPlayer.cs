using System.Collections.Concurrent;

namespace Retune.Mpv.Tests.Support;

/// <summary>
/// An <see cref="MpvClient"/> with no video or audio output, plus helpers to await events.
/// </summary>
internal sealed class HeadlessPlayer : IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly ConcurrentQueue<MpvEvent> _history = new();
    private readonly List<(Func<MpvEvent, bool> Match, TaskCompletionSource<MpvEvent> Done)> _waiters = [];
    private readonly Lock _gate = new();

    public HeadlessPlayer(params KeyValuePair<string, string>[] extraOptions)
    {
        var options = MpvOptions.ForEmbeddedPlayer(windowHandle: null, volume: 100, userAgent: "Retune-Tests/1.0", silent: true)
            .Concat(extraOptions);
        Client = MpvClient.Create(options, logLevel: null);
        Client.EventReceived += OnEvent;
    }

    public MpvClient Client { get; }

    public IReadOnlyCollection<MpvEvent> History => _history;

    public void Load(string path) => Client.CommandAsync("loadfile", path, "replace");

    /// <summary>Waits for the first event (past or future) that matches.</summary>
    public async Task<T> WaitFor<T>(Func<T, bool>? match = null, TimeSpan? timeout = null)
        where T : MpvEvent
    {
        bool Matches(MpvEvent e) => e is T t && (match?.Invoke(t) ?? true);

        TaskCompletionSource<MpvEvent> done;
        lock (_gate)
        {
            var past = _history.FirstOrDefault(Matches);
            if (past is not null)
            {
                return (T)past;
            }

            done = new TaskCompletionSource<MpvEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((Matches, done));
        }

        var result = await done.Task.WaitAsync(timeout ?? DefaultTimeout, TestContext.Current.CancellationToken);
        return (T)result;
    }

    public Task<MpvPropertyChanged> WaitForProperty(string name, Func<object?, bool> match, TimeSpan? timeout = null) =>
        WaitFor<MpvPropertyChanged>(p => p.Name == name && match(p.Value), timeout);

    public void Dispose() => Client.Dispose();

    private void OnEvent(object? sender, MpvEvent e)
    {
        lock (_gate)
        {
            _history.Enqueue(e);
            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                if (_waiters[i].Match(e))
                {
                    _waiters[i].Done.TrySetResult(e);
                    _waiters.RemoveAt(i);
                }
            }
        }
    }
}
