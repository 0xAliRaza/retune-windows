using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Retune.App.Composition;
using Retune.Core.Models;
using Retune.Presentation.ViewModels;

namespace Retune.App.Dev;

/// <summary>
/// Dev-only <c>--perf &lt;report&gt; --demo &lt;url&gt;</c>: drives the real window through a large catalog without any
/// input (load, section switches, layouts, search, scrolling) and writes timings, the longest UI-thread stall and
/// memory to a text report, then closes the app.
/// </summary>
internal static class PerfProbe
{
    private const double WheelStep = 100;

    private static readonly TimeSpan FrameBudget = TimeSpan.FromMilliseconds(16.7);

    public static async Task RunAsync(AppContainer app, Window window, string provider, string reportPath)
    {
        var report = new StringBuilder();
        var stalls = new StallMonitor(window.Dispatcher);
        void Line(string text) => report.AppendLine(text);

        Line($"Retune perf probe {DateTime.Now.ToString("u", CultureInfo.InvariantCulture)}");
        Line(Memory("managed heap before loading"));
        var watch = Stopwatch.StartNew();
        await app.Playlists.AddAsync(new Playlist(Guid.NewGuid(), "Perf", new XtreamSource(provider, "demo"), DateTimeOffset.UtcNow), "demo").ConfigureAwait(true);
        await Idle();
        var counts = app.Playlists.Index!.Catalog;
        Line($"catalog: {counts.Channels.Count} channels, {counts.Movies.Count} movies, {counts.Series.Count} series");
        Line(Row("download + decode + index + first layout", watch.Elapsed));
        Line(Memory("managed heap with the catalog loaded"));

        await Measure("Movies (All, grid)", () =>
        {
            app.Shell.SelectSection(LibrarySection.Movies);
            app.Shell.SetLayout(LayoutMode.Grid);
        });
        await Scroll("scroll Movies grid, 60 pages", window, report, stalls, page: true);
        await Scroll("scroll Movies grid, 150 wheel steps", window, report, stalls, page: false);
        Line(Memory("managed heap after scrolling the grid"));
        await Measure("Movies list layout", () => app.Shell.SetLayout(LayoutMode.List));
        await Scroll("scroll Movies list, 60 pages", window, report, stalls, page: true);
        await Scroll("scroll Movies list, 150 wheel steps", window, report, stalls, page: false);
        await Measure("Movies compact layout", () => app.Shell.SetLayout(LayoutMode.Compact));
        await Measure("Live TV (All)", () => app.Shell.SelectSection(LibrarySection.Live));
        await Measure("Series (All)", () => app.Shell.SelectSection(LibrarySection.Series));
        await MeasureAsync("search \"midnight\" (after the 200 ms debounce)", async () =>
        {
            app.Shell.SearchText = "midnight";
            await Task.Delay(ShellViewModel.SearchDebounce + TimeSpan.FromMilliseconds(20)).ConfigureAwait(true);
        });
        Line($"search results: {app.Shell.Content.Items.Count}");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var process = Process.GetCurrentProcess();
        Line($"managed heap after GC: {GC.GetTotalMemory(forceFullCollection: false) / (1 << 20)} MiB");
        Line($"working set: {process.WorkingSet64 / (1 << 20)} MiB, private bytes: {process.PrivateMemorySize64 / (1 << 20)} MiB");
        Line($"longest UI-thread stall over the whole run: {stalls.Longest.TotalMilliseconds:0} ms");

        await app.Playlists.DeleteAsync(app.Playlists.Active!.Id).ConfigureAwait(true);
        stalls.Stop();
        await File.WriteAllTextAsync(reportPath, report.ToString()).ConfigureAwait(true);
        window.Close();

        Task Measure(string label, Action action) => MeasureAsync(label, () =>
        {
            action();
            return Task.CompletedTask;
        });

        async Task MeasureAsync(string label, Func<Task> action)
        {
            stalls.Reset();
            var w = Stopwatch.StartNew();
            await action().ConfigureAwait(true);
            await Idle();
            Line($"{Row(label, w.Elapsed)}  (longest stall {stalls.Longest.TotalMilliseconds:0} ms)");
        }

    }


    /// <summary>Pages (a full viewport per step, the worst case) or wheel steps (three lines, about 100 px).</summary>
    private static async Task Scroll(string label, Window window, StringBuilder report, StallMonitor stalls, bool page)
    {
        if (FindLargestScroller(window) is not { } scroller)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"{label}: no scroller found");
            return;
        }

        stalls.Reset();
        var frames = new List<double>();
        var total = Stopwatch.StartNew();
        for (var step = 0; step < (page ? 60 : 150) && scroller.VerticalOffset < scroller.ScrollableHeight; step++)
        {
            var w = Stopwatch.StartNew();
            scroller.ScrollToVerticalOffset(scroller.VerticalOffset + (page ? scroller.ViewportHeight : WheelStep));
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            frames.Add(w.Elapsed.TotalMilliseconds);
        }

        var reached = scroller.VerticalOffset;
        frames.Sort();
        var slow = frames.Count(f => f > FrameBudget.TotalMilliseconds);
        report.AppendLine(CultureInfo.InvariantCulture, $"{label}: {frames.Count} steps in {total.ElapsedMilliseconds} ms; per step median {frames[frames.Count / 2]:0.0} ms, p95 {frames[(int)(frames.Count * 0.95)]:0.0} ms, max {frames[^1]:0.0} ms; over one frame: {slow}; scrolled to {reached:0} of {scroller.ScrollableHeight:0} px in {scroller.TemplatedParent?.GetType().Name ?? "?"}");
        scroller.ScrollToTop();
    }

    private static string Memory(string label) => $"{label}: {GC.GetTotalMemory(forceFullCollection: true) / (1 << 20)} MiB";

    private static string Row(string label, TimeSpan elapsed) => $"{label}: {elapsed.TotalMilliseconds:0} ms";

    private static async Task Idle() => await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

    private static ScrollViewer? FindLargestScroller(DependencyObject root)
    {
        ScrollViewer? best = null;
        var pending = new Stack<DependencyObject>([root]);
        while (pending.TryPop(out var node))
        {
            if (node is ScrollViewer { IsVisible: true } s && s.ScrollableHeight > (best?.ScrollableHeight ?? 0))
            {
                best = s;
            }

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                pending.Push(VisualTreeHelper.GetChild(node, i));
            }
        }

        return best;
    }

    /// <summary>Measures how late a high-priority timer fires: the longest time the UI thread was busy.</summary>
    private sealed class StallMonitor
    {
        private readonly DispatcherTimer _timer;
        private readonly Stopwatch _since = Stopwatch.StartNew();

        public StallMonitor(Dispatcher dispatcher)
        {
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(5), DispatcherPriority.Send, (_, _) => Tick(), dispatcher);
            _timer.Start();
        }

        public TimeSpan Longest { get; private set; }

        public void Reset()
        {
            Longest = TimeSpan.Zero;
            _since.Restart();
        }

        public void Stop() => _timer.Stop();

        private void Tick()
        {
            var gap = _since.Elapsed;
            if (gap > Longest)
            {
                Longest = gap;
            }

            _since.Restart();
        }
    }
}
