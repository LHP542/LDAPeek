namespace LDAPeek.Models;

/// <summary>
/// Persistierte Einstellungen. Alle Felder sind nullable bzw. haben
/// Standardwerte, damit eine ältere Datei ohne das jeweilige Feld weiter
/// gelesen werden kann — ein fehlendes Feld darf nie den Start verhindern.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Domäne oder konkreter Domain Controller. Leer = automatisch: LDAPeek
    /// nimmt dann die Anmeldedomäne des angemeldeten Benutzers, und der
    /// DC-Locator sucht sich einen erreichbaren Controller.
    /// </summary>
    public string? Server { get; set; }

    /// <summary>LDAP-Port. Standard 389, mit LDAPS 636.</summary>
    public int Port { get; set; } = 389;

    /// <summary>
    /// LDAP über TLS. Aus bleibt vertretbar, weil der Kerberos-Bind ohnehin
    /// signiert und versiegelt wird (siehe DirectoryService) — LDAPS ist die
    /// Option für Umgebungen, die es erzwingen.
    /// </summary>
    public bool UseTls { get; set; }

    /// <summary>
    /// Such-Basis. Leer = <c>defaultNamingContext</c> aus dem RootDSE, also die
    /// ganze Domäne. Eine engere Basis (z.B. eine OU) macht Suchen spürbar
    /// schneller.
    /// </summary>
    public string? SearchBase { get; set; }

    /// <summary>
    /// Optionales abweichendes Konto (typisch das Admin-Konto). Leer =
    /// integrierte Windows-Anmeldung des laufenden Benutzers.
    /// </summary>
    public string? BindUser { get; set; }

    /// <summary>
    /// Passwort zum <see cref="BindUser"/>, DPAPI-geschützt und mit
    /// <c>ENC1:</c> vorangestellt. Niemals Klartext — siehe SecretProtection.
    /// </summary>
    public string? BindPasswordProtected { get; set; }

    /// <summary>Obergrenze der Treffer pro Suche.</summary>
    public int SearchLimit { get; set; } = 200;

    /// <summary>
    /// Verschachtelte Gruppen mitauflösen (LDAP_MATCHING_RULE_IN_CHAIN).
    /// Standard an — das ist die Frage, für die man das Werkzeug öffnet.
    /// </summary>
    public bool ResolveNestedGroups { get; set; } = true;

    /// <summary>Deaktivierte Konten in der Trefferliste ausblenden.</summary>
    public bool HideDisabledAccounts { get; set; }

    /// <summary>
    /// Gliederung der Gruppenliste. Standard ist der Verschachtelungsbaum: Er
    /// beantwortet die Frage, wegen der man das Fenster geöffnet hat.
    /// </summary>
    public GroupViewMode GroupView { get; set; } = GroupViewMode.Nesting;

    /// <summary>
    /// Update-Kanal. Ein UNC-Pfad oder lokaler Ordner wird als Ordner-Kanal
    /// erkannt, alles mit http(s) als Adresse — der Kanaltyp ergibt sich aus der
    /// Schreibweise, es gibt bewusst keinen zweiten Schalter dafür.
    /// </summary>
    public string? UpdateChannel { get; set; } = @"\\samba01\542$\5424_IT-Basis-Dienste\LDAPeek";

    /// <summary>Beim Start automatisch nach Updates sehen.</summary>
    public bool CheckUpdatesOnStart { get; set; } = true;

    /// <summary>Zuletzt gesuchte Begriffe, neueste zuerst.</summary>
    public List<string> RecentSearches { get; set; } = [];

    /// <summary>Fensterposition und -größe, damit das Werkzeug dort aufgeht, wo es zuletzt stand.</summary>
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }

    public const int MaxRecentSearches = 15;

    /// <summary>
    /// Nimmt einen Suchbegriff in die Verlaufsliste auf: nach vorn, ohne
    /// Duplikate (Groß-/Kleinschreibung egal), auf <see cref="MaxRecentSearches"/>
    /// gekürzt.
    /// </summary>
    public void RememberSearch(string? term)
    {
        if (string.IsNullOrWhiteSpace(term)) return;

        string trimmed = term.Trim();
        RecentSearches.RemoveAll(x => string.Equals(x, trimmed, StringComparison.OrdinalIgnoreCase));
        RecentSearches.Insert(0, trimmed);
        if (RecentSearches.Count > MaxRecentSearches)
        {
            RecentSearches.RemoveRange(MaxRecentSearches, RecentSearches.Count - MaxRecentSearches);
        }
    }

    /// <summary>Tiefe Kopie für das Einstellungen-Fenster (Abbrechen muss verwerfen können).</summary>
    public AppSettings Clone() => new()
    {
        Server = Server,
        Port = Port,
        UseTls = UseTls,
        SearchBase = SearchBase,
        BindUser = BindUser,
        BindPasswordProtected = BindPasswordProtected,
        SearchLimit = SearchLimit,
        ResolveNestedGroups = ResolveNestedGroups,
        HideDisabledAccounts = HideDisabledAccounts,
        GroupView = GroupView,
        UpdateChannel = UpdateChannel,
        CheckUpdatesOnStart = CheckUpdatesOnStart,
        RecentSearches = [.. RecentSearches],
        WindowWidth = WindowWidth,
        WindowHeight = WindowHeight,
    };
}
