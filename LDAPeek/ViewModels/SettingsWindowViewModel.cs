using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LDAPeek.Models;
using LDAPeek.Services;
using NLog;

namespace LDAPeek.ViewModels;

/// <summary>
/// Einstellungen. Arbeitet auf einer Kopie, damit „Abbrechen" wirklich verwirft
/// — sonst wären halb eingetippte Serveradressen sofort scharf.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class SettingsWindowViewModel : ViewModelBase
{
    private static readonly ILogger Log = LogManager.GetCurrentClassLogger();

    private readonly SettingsService _settings;
    private readonly IDirectoryService _directory;

    public SettingsWindowViewModel(SettingsService settings, IDirectoryService directory)
    {
        _settings = settings;
        _directory = directory;

        var current = settings.Current;
        _server = current.Server ?? string.Empty;
        _port = current.Port;
        _useTls = current.UseTls;
        _searchBase = current.SearchBase ?? string.Empty;
        _bindUser = current.BindUser ?? string.Empty;
        // Das gespeicherte Passwort wird NICHT im Klartext ins Feld gelegt.
        // Stattdessen zeigt ein Hinweis, dass eines hinterlegt ist; ein leeres
        // Feld beim Speichern lässt es unverändert.
        _hasStoredPassword = SecretProtection.IsProtected(current.BindPasswordProtected);
        _searchLimit = current.SearchLimit;
        _updateChannel = current.UpdateChannel ?? string.Empty;
        _checkUpdatesOnStart = current.CheckUpdatesOnStart;
        _resolveNestedGroups = current.ResolveNestedGroups;
        _hideDisabledAccounts = current.HideDisabledAccounts;
    }

    [ObservableProperty] private string _server;
    [ObservableProperty] private int _port;
    [ObservableProperty] private bool _useTls;
    [ObservableProperty] private string _searchBase;
    [ObservableProperty] private string _bindUser;
    [ObservableProperty] private string _bindPassword = string.Empty;
    [ObservableProperty] private bool _hasStoredPassword;
    [ObservableProperty] private int _searchLimit;
    [ObservableProperty] private string _updateChannel;
    [ObservableProperty] private bool _checkUpdatesOnStart;
    [ObservableProperty] private bool _resolveNestedGroups;
    [ObservableProperty] private bool _hideDisabledAccounts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isBusy;

    [ObservableProperty] private string? _testResult;
    [ObservableProperty] private bool _testSucceeded;

    /// <summary>Wird true gesetzt, wenn der Nutzer gespeichert hat.</summary>
    public bool Saved { get; private set; }

    /// <summary>Das Fenster hängt sich hier ein, um sich selbst zu schließen.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Anzeigetext der automatisch verwendeten Domäne.</summary>
    public string AutoDomainHint
    {
        get
        {
            try
            {
                return $"Leer lassen = automatisch ({DirectoryService.DefaultDomain()})";
            }
            catch (DirectoryAccessException)
            {
                return "Leer lassen = automatisch (keine Domäne erkannt)";
            }
        }
    }

    partial void OnUseTlsChanged(bool value)
    {
        // Nur die Standardports umschalten. Wer bewusst einen abweichenden Port
        // eingetragen hat, soll ihn behalten.
        if (value && Port == 389) Port = 636;
        else if (!value && Port == 636) Port = 389;
    }

    private bool CanRun() => !IsBusy;

    /// <summary>
    /// Probiert die eingetragenen Werte aus, ohne sie dauerhaft zu speichern.
    /// Dafür werden sie kurz übernommen und danach wieder zurückgerollt — der
    /// Verzeichnisdienst liest seine Konfiguration aus den Einstellungen, und
    /// ein zweiter Weg dorthin wäre eine Fehlerquelle mehr.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        TestResult = "Verbindung wird geprüft …";
        TestSucceeded = false;

        var backup = _settings.Current;
        try
        {
            _settings.Replace(BuildSettings());
            _directory.Invalidate();

            var info = await _directory.ConnectAsync(CancellationToken.None).ConfigureAwait(true);
            TestResult = $"Verbindung steht: {info.StatusText}\nSuch-Basis: {info.SearchBase}";
            TestSucceeded = true;
        }
        catch (DirectoryAccessException ex)
        {
            TestResult = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Verbindungstest fehlgeschlagen.");
            TestResult = "Verbindungstest fehlgeschlagen. Details stehen im Log.";
        }
        finally
        {
            if (!TestSucceeded)
            {
                _settings.Replace(backup);
                _directory.Invalidate();
            }
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Save()
    {
        _settings.Replace(BuildSettings());
        _directory.Invalidate();
        Saved = true;

        Log.Info("Einstellungen gespeichert (Server='{0}', Port={1}, TLS={2}, Basis='{3}').",
            Server, Port, UseTls, SearchBase);

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        Saved = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Löscht ein hinterlegtes Passwort.</summary>
    [RelayCommand]
    private void ClearStoredPassword()
    {
        BindPassword = string.Empty;
        HasStoredPassword = false;
    }

    internal AppSettings BuildSettings()
    {
        var settings = _settings.Current.Clone();

        settings.Server = Trimmed(Server);
        settings.Port = Port is > 0 and <= 65535 ? Port : 389;
        settings.UseTls = UseTls;
        settings.SearchBase = Trimmed(SearchBase);
        settings.BindUser = Trimmed(BindUser);
        settings.SearchLimit = Math.Clamp(SearchLimit, 1, 5000);
        settings.UpdateChannel = Trimmed(UpdateChannel);
        settings.CheckUpdatesOnStart = CheckUpdatesOnStart;
        settings.ResolveNestedGroups = ResolveNestedGroups;
        settings.HideDisabledAccounts = HideDisabledAccounts;

        if (settings.BindUser is null)
        {
            // Kein Konto mehr eingetragen: das Passwort hat keinen Zweck mehr
            // und wird mit entfernt, statt verwaist liegen zu bleiben.
            settings.BindPasswordProtected = null;
        }
        else if (BindPassword.Length > 0)
        {
            settings.BindPasswordProtected = SecretProtection.Protect(BindPassword);
        }
        else if (!HasStoredPassword)
        {
            settings.BindPasswordProtected = null;
        }

        return settings;
    }

    private static string? Trimmed(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
