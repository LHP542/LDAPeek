#if DEBUG
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LDAPeek.Models;
using LDAPeek.Services;
using LDAPeek.ViewModels;
using NLog;

namespace LDAPeek.Views;

/// <summary>
/// Erzeugt die Bildschirmfotos für die Dokumentation — aufgerufen über
/// <c>LDAPeek.exe --screenshots &lt;Zielordner&gt;</c> (nur im Debug-Build).
///
/// <b>Warum im eigenen Prozess und nicht von außen abgegriffen:</b>
/// <c>PrintWindow</c>, <c>SetForegroundWindow</c> und UI-Automation sehen für
/// verhaltensbasierte Virenscanner wie eine Fernsteuerung aus und werden
/// blockiert. Außerdem wäre das Ergebnis von Fokus, DPI und verdeckenden
/// Fenstern abhängig. Avalonia rendert hier direkt in eine Bitmap — reproduzierbar
/// und ohne jedes Fenster auf dem Bildschirm.
///
/// <b>Die Daten sind frei erfunden</b> (<see cref="DemoDirectoryService"/>):
/// Das Repository ist öffentlich, echte Verzeichnisdaten haben dort nichts zu
/// suchen.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ScreenshotTool
{
    private static readonly ILogger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Zielordner, wenn der Schalter gesetzt wurde — sonst null.</summary>
    public static string? OutputDirectory { get; private set; }

    /// <summary>Liest <c>--screenshots &lt;Ordner&gt;</c> aus den Argumenten.</summary>
    public static bool TryParse(string[] args)
    {
        int i = Array.FindIndex(args, a =>
            a.Equals("--screenshots", StringComparison.OrdinalIgnoreCase));

        if (i < 0 || i + 1 >= args.Length) return false;

        OutputDirectory = Path.GetFullPath(args[i + 1]);
        return true;
    }

    /// <summary>
    /// Baut die Fenster mit Demodaten, rendert sie und beendet die Anwendung.
    /// </summary>
    public static async Task RunAsync(Application app, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        // Die Anmeldedomäne wird an mehreren Stellen angezeigt (Statusleiste,
        // Hinweis im Einstellungen-Fenster). Ohne diese Zeile stünde der echte
        // interne Domänenname in Bildern eines öffentlichen Repositories.
        // Wirkt nur in diesem Prozess.
        Environment.SetEnvironmentVariable("USERDNSDOMAIN", "musterstadt.beispiel");
        Environment.SetEnvironmentVariable("USERDOMAIN", "MUSTERSTADT");

        // Einstellungen in den Temp-Ordner, nicht neben die Bilder — im
        // Zielordner sollen nur die PNGs landen.
        var settings = new SettingsService(
            Path.Combine(Path.GetTempPath(), "LDAPeek-Screenshot-Settings.json"));
        settings.Load();
        settings.Current.Server = "musterstadt.beispiel";
        settings.Current.UpdateChannel = @"\\dateiserver\software$\LDAPeek";

        var directory = new DemoDirectoryService();

        // --- Hauptfenster mit Treffer, Stammdaten und Gruppen ---------------
        var mainViewModel = new MainWindowViewModel(directory, settings, new UpdateService(settings));
        var main = new MainWindow(mainViewModel, new EmptyServices());

        await ShowAndSettleAsync(main);
        await mainViewModel.InitializeAsync();

        mainViewModel.SearchText = "muster";
        await mainViewModel.SearchCommand.ExecuteAsync(null);
        mainViewModel.SelectedResult = mainViewModel.Results.FirstOrDefault();

        // Auf die Detail- und Gruppenabfrage warten, sonst landet ein halb
        // gefülltes Fenster im Bild.
        await SettleAsync(main, rounds: 12);
        Save(main, outputDirectory, "hauptfenster.png");

        // --- Abweisungsfenster ---------------------------------------------
        var policy = new AccessPolicy
        {
            RequiredGroup = "RG-LDAPeek-Benutzer",
            ContactHint = "Zugang bitte beim Team IT-Basis-Dienste anfragen.",
            Source = PolicySource.Assembly,
        };
        // autoRecheck: false — die Rückfrage würde gegen die ECHTE
        // Windows-Anmeldung laufen und den Demo-Kontonamen im Bild durch den
        // tatsächlichen ersetzen.
        var denied = new AccessDeniedWindow(
            new AccessGate(policy, directory),
            new AccessCheckResult(false, AccessCheckSource.LogonToken,
                @"MUSTERSTADT\m.mustermann", "RG-LDAPeek-Benutzer", null),
            autoRecheck: false);

        await ShowAndSettleAsync(denied);
        Save(denied, outputDirectory, "kein-zugriff.png");

        // --- Einstellungen ---------------------------------------------------
        var settingsWindow = new SettingsWindow(new SettingsWindowViewModel(settings, directory));
        await ShowAndSettleAsync(settingsWindow);
        Save(settingsWindow, outputDirectory, "einstellungen.png");

        foreach (var w in new Window[] { main, denied, settingsWindow }) w.Close();

        Log.Info("Bildschirmfotos in {0} erzeugt.", outputDirectory);
        if (app.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes
            .IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    /// <summary>
    /// Zeigt das Fenster außerhalb des sichtbaren Bereichs und lässt Layout und
    /// Rendering durchlaufen. Ohne einen echten Show()-Durchlauf hat das Fenster
    /// keine Größe und die Bitmap bliebe leer.
    /// </summary>
    private static async Task ShowAndSettleAsync(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = new PixelPoint(-4000, -4000);
        window.ShowInTaskbar = false;
        window.Show();
        await SettleAsync(window, rounds: 8);
    }

    /// <summary>
    /// Gibt dem Dispatcher mehrfach Gelegenheit, ausstehende Layout-, Render-
    /// und Hintergrundarbeit abzuschließen.
    /// </summary>
    private static async Task SettleAsync(Window window, int rounds)
    {
        for (int i = 0; i < rounds; i++)
        {
            window.UpdateLayout();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(120);
        }
    }

    private static void Save(Window window, string directory, string fileName)
    {
        var size = new PixelSize(
            Math.Max(1, (int)window.Bounds.Width),
            Math.Max(1, (int)window.Bounds.Height));

        using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
        bitmap.Render(window);

        string path = Path.Combine(directory, fileName);
        using var stream = File.Create(path);
        // Bitmap.Save(Stream) ohne Optionen ist ab Avalonia 12.1 deprecated und
        // mit TreatWarningsAsErrors ein Compile-Fehler.
        bitmap.Save(stream, new PngBitmapEncoderOptions());

        Log.Info("{0} geschrieben ({1}x{2}).", fileName, size.Width, size.Height);
    }

    /// <summary>
    /// Das Hauptfenster erwartet einen Dienstanbieter für die Nebenfenster. Für
    /// die Bilder werden die nie geöffnet.
    /// </summary>
    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
#endif
