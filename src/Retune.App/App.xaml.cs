using System.Windows;
using Retune.App.Composition;
using Retune.App.Dev;

namespace Retune.App;

#pragma warning disable CA1001 // Application has no Dispose; both fields are released in OnExit.
public partial class App : Application
#pragma warning restore CA1001
{
    private const string InstanceName = "Retune.SingleInstance.v1";
    private const string PurgeDataArgument = "--purge-data";
    private const string ResumeLastArgument = "--resume-last";

    private Mutex? _instance;
    private AppContainer? _container;

    public AppContainer Container => _container ?? throw new InvalidOperationException("The app hasn't started yet.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains(PurgeDataArgument))
        {
            // Run by the uninstaller when the user also wants their data removed: no window, exit code only.
            Shutdown(PurgeData());
            return;
        }

        // Isolated dev and test runs (RETUNE_DATA_DIR) never meet the user's own Retune: no lock, no bringing it forward.
        var isolated = Infrastructure.Storage.AppPaths.ForCurrentUser().IsIsolated;
        var dev = DevOptions.Parse(e.Args, isolated);
        if (dev.RenderFolder is null && !isolated && !ClaimSingleInstance())
        {
            Shutdown();
            return;
        }

        _container = new AppContainer(Dispatcher);
        var log = _container.Log;
        DispatcherUnhandledException += (_, args) =>
        {
            // Never lose the library on a crash; the exception still surfaces.
            log.Failure("crash", "Unhandled exception on the UI thread.", args.Exception);
            _container?.Library.Flush();
            args.Handled = false;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            log.Failure("crash", "Unhandled exception.", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            log.Failure("background", "A background task failed and nobody observed it.", args.Exception.GetBaseException());
            args.SetObserved();
        };

        if (dev.RenderFolder is { } folder)
        {
            await DevRenderer.RunAsync(_container, dev, folder).ConfigureAwait(true);
            Shutdown();
            return;
        }

        _container.Silent = dev.Mute;
        var window = new MainWindow(_container) { ShowActivated = !dev.NoActivate };
        dev.ApplyBounds(window);
        MainWindow = window;
        SessionEnding += (_, _) => window.PrepareForExit(); // sign-out or restart: Closing isn't raised
        window.Show();
        await _container.Playlists.InitializeAsync().ConfigureAwait(true);
        if (dev is { PerfReport: { } report, DemoProvider: { } provider })
        {
            await PerfProbe.RunAsync(_container, window, provider, report).ConfigureAwait(true);
            return;
        }

        if (dev is { TourFolder: { } tour, DemoProvider: { } tourProvider })
        {
            await ScreenshotTour.RunAsync(_container, window, tourProvider, tour).ConfigureAwait(true);
            return;
        }

        await DevSession.RunAsync(_container, dev).ConfigureAwait(true);
        if (e.Args.Contains(ResumeLastArgument))
        {
            // After an update restart: back on the channel that was playing.
            _container.Shell.ResumeLastChannel();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _container?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private static int PurgeData()
    {
        try
        {
            var paths = Infrastructure.Storage.AppPaths.ForRealUser();
            Infrastructure.Storage.DataPurge.Run(paths, new Infrastructure.Secrets.CredentialManagerSecretStore(paths.SecretPrefix));
            return 0;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or Core.Errors.RetuneException)
        {
            return 1;
        }
    }

    /// <summary>One Retune per user: a second launch just brings the first window forward.</summary>
    private bool ClaimSingleInstance()
    {
        _instance = new Mutex(initiallyOwned: true, $@"Local\{InstanceName}", out var created);
        if (created)
        {
            return true;
        }

        Interop.NativeMethods.ActivateExistingWindow();
        return false;
    }
}
