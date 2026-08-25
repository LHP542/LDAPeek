using System.Runtime.Versioning;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using LDAPeek.Services;
using LDAPeek.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace LDAPeek.Views;

/// <summary>
/// Hauptfenster. Stellt dem ViewModel die wenigen fensterabhängigen Fähigkeiten
/// bereit (Zwischenablage, Dateiauswahl) und öffnet die Nebenfenster.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class MainWindow : ChromeWindow, IShellServices
{
    private static readonly ILogger Log = LogManager.GetCurrentClassLogger();

    private readonly MainWindowViewModel? _viewModel;
    private readonly IServiceProvider? _services;

    /// <summary>Parameterloser Konstruktor für den XAML-Designer.</summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel, IServiceProvider services) : this()
    {
        _viewModel = viewModel;
        _services = services;

        DataContext = viewModel;
        viewModel.AttachShell(this);

        SettingsButton.Click += (_, _) => OpenSettings();
        AboutButton.Click += (_, _) => OpenAbout();

        // Strg+F springt ins Suchfeld, Escape leert es. Beides ist bei einem
        // Nachschlage-Werkzeug der halbe Bedienweg.
        KeyDown += OnKeyDown;

        Opened += async (_, _) =>
        {
            SearchBox.Focus();
            await viewModel.InitializeAsync().ConfigureAwait(true);
        };

        Closing += (_, _) => viewModel.PersistOnExit();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            SearchBox.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5 && _viewModel?.RefreshCommand.CanExecute(null) == true)
        {
            _viewModel.RefreshCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _viewModel is not null)
        {
            _viewModel.SearchText = string.Empty;
            SearchBox.Focus();
            e.Handled = true;
        }
    }

    private async void OpenSettings()
    {
        if (_services is null || _viewModel is null) return;

        try
        {
            var vm = _services.GetRequiredService<SettingsWindowViewModel>();
            var window = new SettingsWindow(vm);
            await window.ShowDialog(this);

            if (vm.Saved) await _viewModel.OnSettingsChangedAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Einstellungen-Fenster konnte nicht geöffnet werden.");
        }
    }

    private async void OpenAbout()
    {
        if (_services is null) return;

        try
        {
            var updates = _services.GetRequiredService<UpdateService>();
            await new AboutWindow(updates, this).ShowDialog(this);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Über-Fenster konnte nicht geöffnet werden.");
        }
    }

    // ------------------------------------------------------------------
    // IShellServices
    // ------------------------------------------------------------------

    public async Task CopyToClipboardAsync(string text)
    {
        try
        {
            // Avalonia 12: Clipboard läuft über IAsyncDataTransfer; SetTextAsync
            // ist eine Erweiterungsmethode aus Avalonia.Input.Platform.
            if (Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Text konnte nicht in die Zwischenablage gelegt werden.");
        }
    }

    public async Task<string?> SaveTextFileAsync(string suggestedFileName, string content)
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Gruppen exportieren",
                SuggestedFileName = suggestedFileName,
                DefaultExtension = "csv",
                FileTypeChoices =
                [
                    new FilePickerFileType("CSV-Datei") { Patterns = ["*.csv"] },
                ],
            });

            if (file is null) return null;

            await using var stream = await file.OpenWriteAsync();
            // UTF-8 MIT BOM: ohne das öffnet Excel die Datei als ANSI und macht
            // aus jedem Umlaut zwei Zeichen — bei deutschen Gruppennamen also
            // aus jeder zweiten Zeile Müll.
            await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            await writer.WriteAsync(content);

            return file.Path.LocalPath;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Export nach {0} fehlgeschlagen.", suggestedFileName);
            return null;
        }
    }

    public async Task OpenExternalAsync(string target)
    {
        try
        {
            await Launcher.LaunchUriAsync(new Uri(target));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "{0} konnte nicht geöffnet werden.", target);
        }
    }
}
