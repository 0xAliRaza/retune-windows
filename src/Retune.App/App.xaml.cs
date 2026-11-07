using System.Windows;
using Retune.App.Composition;
using Retune.App.Dev;

namespace Retune.App;

#pragma warning disable CA1001 // Application has no Dispose; both fields are released in OnExit.
public partial class App : Application
#pragma warning restore CA1001
{
    private const string InstanceName = "Retune.SingleInstance.v1";

    private Mutex? _instance;
    private AppContainer? _container;

    public AppContainer Container => _container ?? throw new InvalidOperationException("The app hasn't started yet.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var dev = DevOptions.Parse(e.Args);

        if (dev.RenderFolder is null && !ClaimSingleInstance())
        {
            Shutdown();
            return;
        }

        _container = new AppContainer(Dispatcher);
        DispatcherUnhandledException += (_, args) =>
        {
            // Never lose the library on a crash; the exception still surfaces.
            _container?.Library.Flush();
            args.Handled = false;
        };

        if (dev.RenderFolder is { } folder)
        {
            await DevRenderer.RunAsync(_container, dev, folder).ConfigureAwait(true);
            Shutdown();
            return;
        }

        var window = new MainWindow(_container) { ShowActivated = !dev.NoActivate };
        dev.ApplyBounds(window);
        MainWindow = window;
        window.Show();
        await _container.Playlists.InitializeAsync().ConfigureAwait(true);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _container?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>One Retune per user: a second launch just brings the first window forward.</summary>
    private bool ClaimSingleInstance()
    {
        _instance = new Mutex(initiallyOwned: true, $@"Local\{InstanceName}", out var created);
        if (created)
        {
            return true;
        }

        Interop.NativeMethods.ActivateExistingWindow("Retune");
        return false;
    }
}
