using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
            _services = BuildServices();

            var mainViewModel = _services.GetRequiredService<MainWindowViewModel>();
            var window = new MainWindow(mainViewModel, _services);
            desktop.MainWindow = window;

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
                _guard?.Dispose();
                (_services.GetService<IDirectoryService>() as IDisposable)?.Dispose();
                _services.Dispose();
                LogManager.Shutdown();
            };
        }

        base.OnFrameworkInitializationCompleted();
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

        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<SettingsWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
