using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LDAPeek.Models;
using LDAPeek.Services;
using NLog;

namespace LDAPeek.ViewModels;

/// <summary>
/// Zustand des Hauptfensters: Suche, Trefferliste, Benutzerdetails.
/// Die Gruppenlogik liegt im zweiten Teil dieser Klasse
/// (<c>MainWindowViewModel.Groups.cs</c>), damit keine Datei zum Sammelbecken
/// wird.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private static readonly ILogger Log = LogManager.GetCurrentClassLogger();

    private readonly IDirectoryService _directory;
    private readonly SettingsService _settings;
    private readonly UpdateService _updates;

    private IShellServices? _shell;
    private CancellationTokenSource? _searchCts;

    /// <summary>
    /// Verhindert, dass die automatische Auswahl beim Befüllen der Trefferliste
    /// eine Detailabfrage auslöst, bevor der Nutzer überhaupt etwas angeklickt
    /// hat.
    /// </summary>
    private bool _suppressSelectionLoad;

    public MainWindowViewModel(IDirectoryService directory, SettingsService settings, UpdateService updates)
    {
        _directory = directory;
        _settings = settings;
        _updates = updates;

        IncludeNestedGroups = settings.Current.ResolveNestedGroups;
        HideDisabledAccounts = settings.Current.HideDisabledAccounts;
        GroupView = settings.Current.GroupView;
        RecentSearches = new ObservableCollection<string>(settings.Current.RecentSearches);
    }

    /// <summary>Wird vom Fenster nach dem Laden gesetzt.</summary>
    internal void AttachShell(IShellServices shell) => _shell = shell;

    // ------------------------------------------------------------------
    // Suche
    // ------------------------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyGroupsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportGroupsCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Bereit.";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _connectionText = "Nicht verbunden";

    /// <summary>Zuletzt gesuchte Begriffe für das Verlaufs-Dropdown.</summary>
    public ObservableCollection<string> RecentSearches { get; }

    public ObservableCollection<UserSearchHit> Results { get; } = [];

    [ObservableProperty]
    private bool _hideDisabledAccounts;

    partial void OnHideDisabledAccountsChanged(bool value)
    {
        _settings.Current.HideDisabledAccounts = value;
        _settings.Save();

        // Der Filter wirkt serverseitig nicht — die Trefferliste wird also neu
        // aufgebaut, ohne erneut zu suchen.
        ApplyResultFilter();
    }

    /// <summary>Alle Treffer der letzten Suche, ungefiltert.</summary>
    private IReadOnlyList<UserSearchHit> _lastHits = [];

    [ObservableProperty]
    private UserSearchHit? _selectedResult;

    partial void OnSelectedResultChanged(UserSearchHit? value)
    {
        if (_suppressSelectionLoad || value is null) return;
        _ = LoadSelectedUserAsync(value.DistinguishedName);
    }

    private bool CanSearch() => !IsBusy && SearchText.Trim().Length >= 2;

    /// <summary>
    /// Sucht Benutzerkonten. Eine laufende Suche wird abgebrochen — sonst
    /// überholt bei schnellem Tippen eine ältere Antwort die neuere und die
    /// Liste zeigt Treffer zu einem Begriff, der gar nicht mehr im Feld steht.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        string query = SearchText.Trim();

        await CancelRunningSearchAsync().ConfigureAwait(true);
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        IsBusy = true;
        ErrorMessage = null;
        StatusText = $"Suche nach „{query}\" …";

        try
        {
            var info = await _directory.ConnectAsync(token).ConfigureAwait(true);
            ConnectionText = info.StatusText;

            _lastHits = await _directory.SearchUsersAsync(query, token).ConfigureAwait(true);
            ApplyResultFilter();

            RememberSearch(query);

            StatusText = _lastHits.Count switch
            {
                0 => $"Keine Treffer für „{query}\".",
                1 => "1 Treffer.",
                _ => $"{Results.Count} von {_lastHits.Count} Treffern angezeigt.",
            };

            // Genau ein Treffer: direkt öffnen. Das ist der Normalfall, wenn
            // jemand einen Anmeldenamen eintippt, und spart einen Klick.
            if (Results.Count == 1) SelectedResult = Results[0];
        }
        catch (OperationCanceledException)
        {
            Log.Debug("Suche nach '{0}' abgebrochen.", query);
        }
        catch (DirectoryAccessException ex)
        {
            Fail(ex.Message, ex);
        }
        catch (Exception ex)
        {
            Fail("Die Suche ist fehlgeschlagen. Details stehen im Log.", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyResultFilter()
    {
        var visible = HideDisabledAccounts
            ? _lastHits.Where(h => !h.IsDisabled).ToList()
            : _lastHits.ToList();

        // Auswahl über den Neuaufbau retten: ListBox nullt SelectedItem beim
        // Clear() und zieht das per TwoWay-Binding ins ViewModel durch.
        var previous = SelectedResult?.DistinguishedName;

        _suppressSelectionLoad = true;
        try
        {
            Results.Clear();
            foreach (var hit in visible) Results.Add(hit);

            SelectedResult = previous is null
                ? null
                : Results.FirstOrDefault(h =>
                    h.DistinguishedName.Equals(previous, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _suppressSelectionLoad = false;
        }
    }

    private void RememberSearch(string query)
    {
        _settings.Current.RememberSearch(query);
        _settings.Save();

        RecentSearches.Clear();
        foreach (string term in _settings.Current.RecentSearches) RecentSearches.Add(term);
    }

    private async Task CancelRunningSearchAsync()
    {
        if (_searchCts is null) return;

        await _searchCts.CancelAsync().ConfigureAwait(true);
        _searchCts.Dispose();
        _searchCts = null;
    }

    // ------------------------------------------------------------------
    // Benutzerdetails
    // ------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUser))]
    [NotifyCanExecuteChangedFor(nameof(CopyUserSummaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private AdUser? _selectedUser;

    [ObservableProperty]
    private Bitmap? _selectedUserPhoto;

    public bool HasUser => SelectedUser is not null;

    private async Task LoadSelectedUserAsync(string distinguishedName)
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusText = "Benutzer wird geladen …";

        try
        {
            var token = _searchCts?.Token ?? CancellationToken.None;
            var user = await _directory.LoadUserAsync(distinguishedName, token).ConfigureAwait(true);

            SelectedUser = user;
            SelectedUserPhoto = CreatePhoto(user?.Photo);

            if (user is null)
            {
                StatusText = "Das Konto konnte nicht geladen werden.";
                ClearGroups();
                return;
            }

            StatusText = $"{user.BestName} geladen.";
            await LoadGroupsAsync(user).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Der Nutzer hat weitergesucht — kein Fehler.
        }
        catch (DirectoryAccessException ex)
        {
            Fail(ex.Message, ex);
        }
        catch (Exception ex)
        {
            Fail("Das Konto konnte nicht geladen werden. Details stehen im Log.", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Wandelt <c>thumbnailPhoto</c> in ein Bitmap. Der Inhalt ist ein
    /// beliebiges, oft schlecht gepflegtes Bild aus dem Verzeichnis — ein
    /// kaputter Wert darf die Detailansicht nicht verhindern.
    /// </summary>
    private static Bitmap? CreatePhoto(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;

        try
        {
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "thumbnailPhoto konnte nicht gelesen werden ({0} Byte).", bytes.Length);
            return null;
        }
    }

    private bool CanRefresh() => !IsBusy && SelectedUser is not null;

    /// <summary>Lädt das aktuell angezeigte Konto samt Gruppen neu.</summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (SelectedUser is null) return;
        await LoadSelectedUserAsync(SelectedUser.DistinguishedName).ConfigureAwait(true);
    }

    private bool CanCopyUserSummary() => SelectedUser is not null;

    /// <summary>Kopiert eine lesbare Zusammenfassung des Kontos in die Zwischenablage.</summary>
    [RelayCommand(CanExecute = nameof(CanCopyUserSummary))]
    private async Task CopyUserSummaryAsync()
    {
        if (SelectedUser is not { } user || _shell is null) return;

        await _shell.CopyToClipboardAsync(UserReport.Build(user, Groups)).ConfigureAwait(true);
        StatusText = "Zusammenfassung in die Zwischenablage kopiert.";
    }

    // ------------------------------------------------------------------
    // Start und Fehlerbehandlung
    // ------------------------------------------------------------------

    /// <summary>
    /// Baut die Verbindung im Hintergrund auf, damit die erste Suche nicht auf
    /// den Kerberos-Bind warten muss, und sieht nach einem Update.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            var info = await _directory.ConnectAsync(CancellationToken.None).ConfigureAwait(true);
            ConnectionText = info.StatusText;
            StatusText = "Bereit. Bitte einen Namen, Anmeldenamen oder eine E-Mail-Adresse eingeben.";
        }
        catch (DirectoryAccessException ex)
        {
            ConnectionText = "Nicht verbunden";
            Fail(ex.Message, ex);
        }
        catch (Exception ex)
        {
            ConnectionText = "Nicht verbunden";
            Fail("Die Verbindung zum Verzeichnis ist fehlgeschlagen. Details stehen im Log.", ex);
        }

        if (_settings.Current.CheckUpdatesOnStart) await CheckForUpdateAsync().ConfigureAwait(true);
    }

    [ObservableProperty]
    private string? _updateNotice;

    private async Task CheckForUpdateAsync()
    {
        try
        {
            var result = await _updates.CheckForUpdateAsync().ConfigureAwait(true);
            UpdateNotice = result.UpdateAvailable
                ? $"Version {result.LatestVersion} ist verfügbar — im Über-Fenster installierbar."
                : null;
        }
        catch (Exception ex)
        {
            // Update-Fehler sind nie ein Grund, den Nutzer zu stören.
            Log.Warn(ex, "Update-Prüfung beim Start fehlgeschlagen.");
        }
    }

    /// <summary>Wird nach dem Schließen des Einstellungen-Fensters gerufen.</summary>
    public async Task OnSettingsChangedAsync()
    {
        IncludeNestedGroups = _settings.Current.ResolveNestedGroups;
        HideDisabledAccounts = _settings.Current.HideDisabledAccounts;

        // Ohne Invalidate arbeitet das Werkzeug weiter gegen die alte Sitzung
        // und die geänderte Servereinstellung wirkt scheinbar nicht.
        _directory.Invalidate();
        ConnectionText = "Nicht verbunden";

        try
        {
            var info = await _directory.ConnectAsync(CancellationToken.None).ConfigureAwait(true);
            ConnectionText = info.StatusText;
            StatusText = "Verbindung neu aufgebaut.";
            ErrorMessage = null;
        }
        catch (DirectoryAccessException ex)
        {
            Fail(ex.Message, ex);
        }
    }

    /// <summary>Blendet die Fehlerzeile aus.</summary>
    [RelayCommand]
    private void DismissError() => ErrorMessage = null;

    private void Fail(string userMessage, Exception ex)
    {
        Log.Error(ex, "{0}", userMessage);
        ErrorMessage = userMessage;
        StatusText = "Fehler.";
    }

    /// <summary>Speichert offene Zustände beim Beenden.</summary>
    public void PersistOnExit()
    {
        _settings.Current.ResolveNestedGroups = IncludeNestedGroups;
        _settings.Current.HideDisabledAccounts = HideDisabledAccounts;
        _settings.Save();
    }
}
