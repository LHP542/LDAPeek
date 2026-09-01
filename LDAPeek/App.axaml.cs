using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LDAPeek.Models;
using LDAPeek.Services;
using LDAPeek.ViewModels;
using LDAPeek.Views;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace LDAPeek;

[SupportedOSPlatform("windows")]
public partial class App : Application
{
    private static readonly ILogger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Der in <c>Program.Main</c> beanspruchte Guard. Wird hier übernommen,
    /// damit ein zweiter Start das bestehende Fenster nach vorn holt.
    /// </summary>
    internal static SingleInstanceGuard? PendingGuard { get; set; }

    private ServiceProvider? _services;
    private SingleInstanceGuard? _guard;

    // GC-Referenz: ohne ein Feld sammelt der Garbage Collector das Tray-Icon
    // ein und es verschwindet nach einiger Laufzeit „zufällig".
    private TrayController? _tray;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
#if DEBUG
            // Werkzeugmodus für die Dokumentationsbilder — kein Verzeichnis,
            // keine Zugangsprüfung, ausschließlich Demodaten.
            if (ScreenshotTool.OutputDirectory is { } screenshotDir)
            {
                _ = ScreenshotTool.RunAsync(this, screenshotDir);
                base.OnFrameworkInitializationCompleted();
                return;
            }
#endif

            _services = BuildServices();

            // Zugangsprüfung vor allem anderen. Bewusst nur die Token-Variante:
            // sie ist synchron und kostet Mikrosekunden, während eine
            // LDAP-Abfrage hier den Start um Sekunden verzögern würde — und das
            // im Normalfall, in dem der Nutzer ohnehin berechtigt ist. Die
            // Rückfrage im Verzeichnis übernimmt das Abweisungsfenster.
            var gate = _services.GetRequiredService<AccessGate>();
            var check = gate.CheckToken();

            // In beiden Zweigen KEIN eigenes Show(): die Desktop-Lifetime zeigt
            // das gesetzte MainWindow nach dieser Methode selbst. Ein zusätzlicher
            // Show()-Aufruf lässt "Opened" ein zweites Mal feuern — im
            // Abweisungsfenster lief die Verzeichnis-Rückfrage dadurch doppelt.
            if (!check.Granted)
            {
                ShowAccessDenied(desktop, gate, check);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            StartMainWindow(desktop, show: false);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Zeigt das Abweisungsfenster. Bestätigt die dortige Rückfrage den Zugang
    /// doch noch, startet LDAPeek regulär weiter.
    /// </summary>
    private void ShowAccessDenied(
        IClassicDesktopStyleApplicationLifetime desktop, AccessGate gate, AccessCheckResult check)
    {
        // Ohne das würde das Schließen des Abweisungsfensters die Anwendung
        // beenden, bevor das Hauptfenster überhaupt entstehen kann.
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var denied = new AccessDeniedWindow(gate, check);
        desktop.MainWindow = denied;

        denied.Closed += (_, _) =>
        {
            if (denied.AccessGranted)
            {
                // Hier ist der Startvorgang der Lifetime vorbei — dieses Fenster
                // muss selbst gezeigt werden.
                StartMainWindow(desktop, show: true);
                return;
            }

            Log.Info("LDAPeek wird ohne Zugang beendet.");
            Cleanup();
            desktop.Shutdown();
        };
    }

    private void StartMainWindow(IClassicDesktopStyleApplicationLifetime desktop, bool show)
    {
        var mainViewModel = _services!.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow(mainViewModel, _services!);

        desktop.MainWindow = window;
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

        _tray = new TrayController(this, window);
        _tray.Install();

        _guard = PendingGuard;
        if (_guard is not null)
        {
            _guard.ActivationRequested += (_, _) => _tray?.Restore();
            _guard.StartListening();
        }

        desktop.Exit += (_, _) =>
        {
            Log.Info("LDAPeek wird beendet.");
            mainViewModel.PersistOnExit();
            Cleanup();
        };

        if (show) window.Show();
    }

    private void Cleanup()
    {
        _guard?.Dispose();
        (_services?.GetService<IDirectoryService>() as IDisposable)?.Dispose();
        _services?.Dispose();
        LogManager.Shutdown();
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        // Einstellungen werden einmal geladen und dann von allen geteilt — der
        // Verzeichnisdienst liest daraus seine Verbindungsparameter.
        var settings = new SettingsService();
        settings.Load();
        services.AddSingleton(settings);

        services.AddSingleton<IDirectoryService, DirectoryService>();
        services.AddSingleton<UpdateService>();

        // Zugangsregel liegt neben der EXE und gehört zur Auslieferung, nicht
        // zum Benutzerprofil — siehe AccessPolicy.
        services.AddSingleton(AccessPolicy.Load());
        services.AddSingleton<AccessGate>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<SettingsWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
