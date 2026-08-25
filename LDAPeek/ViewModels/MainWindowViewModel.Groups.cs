using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LDAPeek.Models;
using LDAPeek.Services;

namespace LDAPeek.ViewModels;

/// <summary>
/// Zweiter Teil des Hauptfenster-ViewModels: alles rund um die
/// Gruppenmitgliedschaften — laden, filtern, exportieren.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class MainWindowViewModel
{
    /// <summary>Die angezeigten (gefilterten) Gruppen.</summary>
    public ObservableCollection<AdGroup> Groups { get; } = [];

    /// <summary>Vollständiges Ergebnis der letzten Gruppenabfrage.</summary>
    private IReadOnlyList<AdGroup> _allGroups = [];

    [ObservableProperty]
    private bool _isLoadingGroups;

    [ObservableProperty]
    private string _groupSummary = "Keine Gruppen geladen.";

    [ObservableProperty]
    private string _groupFilter = string.Empty;

    partial void OnGroupFilterChanged(string value) => ApplyGroupFilter();

    [ObservableProperty]
    private bool _includeNestedGroups;

    partial void OnIncludeNestedGroupsChanged(bool value)
    {
        _settings.Current.ResolveNestedGroups = value;
        _settings.Save();

        // Der Schalter ändert, was der Domain Controller liefert — also neu
        // abfragen statt nur die vorhandene Liste zu filtern.
        if (SelectedUser is { } user) _ = LoadGroupsAsync(user);
    }

    /// <summary>
    /// Nur Sicherheitsgruppen anzeigen. Verteilergruppen tragen keine
    /// Berechtigungen; wer einer Zugriffsfrage nachgeht, will sie meist nicht
    /// sehen.
    /// </summary>
    [ObservableProperty]
    private bool _securityGroupsOnly;

    partial void OnSecurityGroupsOnlyChanged(bool value) => ApplyGroupFilter();

    private void ClearGroups()
    {
        _allGroups = [];
        Groups.Clear();
        GroupSummary = "Keine Gruppen geladen.";
    }

    /// <summary>
    /// Lädt die Gruppen des Kontos. Läuft getrennt von der Detailabfrage, damit
    /// die Stammdaten schon stehen, während die — bei tief verschachtelten
    /// Strukturen spürbar langsamere — Gruppenauflösung noch arbeitet.
    /// </summary>
    private async Task LoadGroupsAsync(AdUser user)
    {
        IsLoadingGroups = true;
        GroupSummary = IncludeNestedGroups
            ? "Gruppen werden aufgelöst (inklusive verschachtelter) …"
            : "Direkte Gruppen werden geladen …";

        try
        {
            var token = _searchCts?.Token ?? CancellationToken.None;
            _allGroups = await _directory
                .LoadGroupsAsync(user, IncludeNestedGroups, token)
                .ConfigureAwait(true);

            ApplyGroupFilter();
        }
        catch (OperationCanceledException)
        {
            ClearGroups();
        }
        catch (DirectoryAccessException ex)
        {
            ClearGroups();
            Fail(ex.Message, ex);
        }
        catch (Exception ex)
        {
            ClearGroups();
            Fail("Die Gruppen konnten nicht geladen werden. Details stehen im Log.", ex);
        }
        finally
        {
            IsLoadingGroups = false;
        }
    }

    private void ApplyGroupFilter()
    {
        string filter = GroupFilter.Trim();

        IEnumerable<AdGroup> visible = _allGroups;
        if (SecurityGroupsOnly) visible = visible.Where(g => g.Kind == GroupKind.Security);
        if (filter.Length > 0) visible = visible.Where(g => MatchesFilter(g, filter));

        Groups.Clear();
        foreach (var group in visible) Groups.Add(group);

        GroupSummary = BuildGroupSummary(filter.Length > 0 || SecurityGroupsOnly);
        CopyGroupsCommand.NotifyCanExecuteChanged();
        ExportGroupsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Sucht in Name, Anmeldename und Beschreibung. Der DN bleibt bewusst außen
    /// vor: er enthält den Gruppennamen ohnehin und würde bei einer Suche nach
    /// einer OU alle Gruppen darunter zu Treffern machen.
    /// </summary>
    internal static bool MatchesFilter(AdGroup group, string filter) =>
        Contains(group.DisplayName, filter)
        || Contains(group.SamAccountName, filter)
        || Contains(group.Description, filter);

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.CurrentCultureIgnoreCase);

    private string BuildGroupSummary(bool filtered)
    {
        if (_allGroups.Count == 0) return "Keine Gruppenmitgliedschaften gefunden.";

        int direct = _allGroups.Count(g => g.Membership == MembershipKind.Direct);
        int nested = _allGroups.Count(g => g.Membership == MembershipKind.Nested);
        int primary = _allGroups.Count(g => g.Membership == MembershipKind.Primary);

        var parts = new List<string> { $"{direct} direkt" };
        if (IncludeNestedGroups) parts.Add($"{nested} verschachtelt");
        if (primary > 0) parts.Add($"{primary} Primärgruppe");

        string basis = $"{_allGroups.Count} Gruppen ({string.Join(", ", parts)})";
        return filtered ? $"{Groups.Count} von {basis}" : basis;
    }

    private bool CanExportGroups() => !IsBusy && Groups.Count > 0;

    /// <summary>Kopiert die angezeigten Gruppen als CSV in die Zwischenablage.</summary>
    [RelayCommand(CanExecute = nameof(CanExportGroups))]
    private async Task CopyGroupsAsync()
    {
        if (_shell is null) return;

        await _shell.CopyToClipboardAsync(BuildGroupCsv()).ConfigureAwait(true);
        StatusText = $"{Groups.Count} Gruppen in die Zwischenablage kopiert.";
    }

    /// <summary>Speichert die angezeigten Gruppen als CSV-Datei.</summary>
    [RelayCommand(CanExecute = nameof(CanExportGroups))]
    private async Task ExportGroupsAsync()
    {
        if (_shell is null) return;

        string name = SelectedUser?.SamAccountName ?? "Gruppen";
        string? path = await _shell
            .SaveTextFileAsync($"{name}-Gruppen.csv", BuildGroupCsv())
            .ConfigureAwait(true);

        StatusText = path is null
            ? "Export abgebrochen."
            : $"{Groups.Count} Gruppen nach {path} exportiert.";
    }

    private string BuildGroupCsv()
    {
        var lines = new List<string> { AdGroup.CsvHeader };
        lines.AddRange(Groups.Select(g => g.ToCsvRow()));

        // Windows-Zeilenenden: die Datei landet in Excel, nicht in einem
        // Unix-Werkzeug.
        return string.Join("\r\n", lines);
    }
}
