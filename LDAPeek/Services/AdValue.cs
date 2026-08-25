using System.Globalization;
using LDAPeek.Models;

namespace LDAPeek.Services;

/// <summary>
/// Übersetzt AD-Rohwerte in etwas, das man einem Menschen zeigen kann.
///
/// Der Grund für eine eigene Klasse: AD kodiert Zeitstempel und Zustände in
/// mehreren, zueinander inkompatiblen Formaten, und jedes hat eigene
/// Sonderwerte, die „nie" bedeuten. Wer die verwechselt, zeigt dem Nutzer
/// „Konto läuft ab am 01.01.1601" statt „läuft nicht ab" — beides sind
/// vermeidbare Support-Anrufe.
/// </summary>
internal static class AdValue
{
    /// <summary>
    /// Sonderwert in <c>accountExpires</c> und <c>msDS-UserPasswordExpiryTimeComputed</c>
    /// für „läuft nie ab".
    /// </summary>
    public const long Never = long.MaxValue;

    /// <summary>
    /// Wandelt einen Windows-FILETIME-Wert (100-ns-Ticks seit 1601-01-01 UTC) in
    /// lokale Zeit. Liefert <c>null</c> für die beiden „kein Wert"-Kodierungen
    /// 0 und <see cref="Never"/> sowie für Werte außerhalb des DateTime-Bereichs.
    /// </summary>
    public static DateTimeOffset? FromFileTime(long fileTime)
    {
        if (fileTime <= 0 || fileTime >= Never) return null;
        try
        {
            return DateTimeOffset.FromFileTime(fileTime).ToLocalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            // Kaputte Werte kommen in gewachsenen Domänen tatsächlich vor
            // (Migrationen, fehlerhafte Provisionierungsskripte).
            return null;
        }
    }

    /// <summary>
    /// Wandelt einen AD-Generalized-Time-String (<c>whenCreated</c>,
    /// <c>whenChanged</c>: <c>yyyyMMddHHmmss.0Z</c>) in lokale Zeit.
    /// </summary>
    public static DateTimeOffset? FromGeneralizedTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        // Der Bruchteil-Anteil (".0") ist optional und variiert je nach DC.
        string trimmed = value.Trim();
        int dot = trimmed.IndexOf('.');
        string core = dot >= 0 ? trimmed[..dot] : trimmed.TrimEnd('Z');

        return DateTime.TryParseExact(core, "yyyyMMddHHmmss",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? new DateTimeOffset(parsed, TimeSpan.Zero).ToLocalTime()
            : null;
    }

    /// <summary>
    /// <c>pwdLastSet == 0</c> heißt „Benutzer muss das Passwort bei der nächsten
    /// Anmeldung ändern" — nicht „nie gesetzt". Der Unterschied ist für den
    /// Helpdesk der eigentlich interessante Teil.
    /// </summary>
    public static bool MustChangePasswordAtNextLogon(long pwdLastSet) => pwdLastSet == 0;

    /// <summary>
    /// <c>lockoutTime</c> ist 0 (bzw. fehlt) bei nicht gesperrten Konten. Ein
    /// Wert &gt; 0 heißt gesperrt — ob die Sperre abgelaufen ist, hängt an der
    /// Domänen-Richtlinie (<c>lockoutDuration</c>), die wir hier bewusst nicht
    /// mitlesen: der Zeitpunkt allein ist die ehrlichere Auskunft.
    /// </summary>
    public static DateTimeOffset? LockedSince(long lockoutTime) => FromFileTime(lockoutTime);

    /// <summary>Zerlegt <c>userAccountControl</c> in benannte Flags.</summary>
    public static AccountFlags ParseAccountFlags(int userAccountControl) =>
        (AccountFlags)userAccountControl;

    /// <summary>Deutsche Klartextnamen der gesetzten Konto-Flags, für die Anzeige.</summary>
    public static IReadOnlyList<string> DescribeAccountFlags(AccountFlags flags)
    {
        var result = new List<string>();
        if (flags.HasFlag(AccountFlags.Disabled)) result.Add("Deaktiviert");
        if (flags.HasFlag(AccountFlags.DontExpirePassword)) result.Add("Passwort läuft nie ab");
        if (flags.HasFlag(AccountFlags.PasswordNotRequired)) result.Add("Kein Passwort erforderlich");
        if (flags.HasFlag(AccountFlags.PasswordCantChange)) result.Add("Passwort nicht änderbar");
        if (flags.HasFlag(AccountFlags.SmartcardRequired)) result.Add("Smartcard erforderlich");
        if (flags.HasFlag(AccountFlags.TrustedForDelegation)) result.Add("Für Delegierung zugelassen");
        if (flags.HasFlag(AccountFlags.NotDelegated)) result.Add("Gegen Delegierung geschützt");
        if (flags.HasFlag(AccountFlags.UseDesKeyOnly)) result.Add("Nur DES-Verschlüsselung");
        if (flags.HasFlag(AccountFlags.DontRequirePreauth)) result.Add("Keine Kerberos-Vorauthentifizierung");
        if (flags.HasFlag(AccountFlags.PasswordExpired)) result.Add("Passwort abgelaufen");
        return result;
    }

    // groupType-Bits (MS-ADTS 2.2.12). SECURITY_ENABLED setzt das Vorzeichenbit,
    // deshalb wird der Wert grundsätzlich als uint betrachtet — ein Vergleich
    // gegen 0x80000000 auf einem int ist entweder ein Compile-Fehler oder,
    // schlimmer, still falsch.
    private const uint GroupBuiltinLocal = 0x00000001;
    private const uint GroupGlobal = 0x00000002;
    private const uint GroupDomainLocal = 0x00000004;
    private const uint GroupUniversal = 0x00000008;
    private const uint GroupSecurityEnabled = 0x80000000;

    /// <summary>Sicherheits- oder Verteilergruppe?</summary>
    public static GroupKind ParseGroupKind(int groupType) =>
        (unchecked((uint)groupType) & GroupSecurityEnabled) != 0
            ? GroupKind.Security
            : GroupKind.Distribution;

    /// <summary>Gültigkeitsbereich der Gruppe.</summary>
    public static GroupScope ParseGroupScope(int groupType)
    {
        uint t = unchecked((uint)groupType);
        // Builtin zuerst prüfen: vordefinierte Gruppen tragen zusätzlich das
        // DomainLocal-Bit und würden sonst als gewöhnliche lokale Gruppe gelten.
        if ((t & GroupBuiltinLocal) != 0) return GroupScope.BuiltinLocal;
        if ((t & GroupGlobal) != 0) return GroupScope.Global;
        if ((t & GroupUniversal) != 0) return GroupScope.Universal;
        if ((t & GroupDomainLocal) != 0) return GroupScope.DomainLocal;
        return GroupScope.Unknown;
    }

    /// <summary>Deutscher Anzeigename für einen Gültigkeitsbereich.</summary>
    public static string DescribeScope(GroupScope scope) => scope switch
    {
        GroupScope.DomainLocal => "Lokal in Domäne",
        GroupScope.Global => "Global",
        GroupScope.Universal => "Universell",
        GroupScope.BuiltinLocal => "Vordefiniert",
        _ => "Unbekannt",
    };

    /// <summary>Deutscher Anzeigename für die Gruppenart.</summary>
    public static string DescribeKind(GroupKind kind) =>
        kind == GroupKind.Security ? "Sicherheit" : "Verteiler";

    /// <summary>Deutscher Anzeigename für die Herkunft einer Mitgliedschaft.</summary>
    public static string DescribeMembership(MembershipKind kind) => kind switch
    {
        MembershipKind.Direct => "direkt",
        MembershipKind.Nested => "verschachtelt",
        MembershipKind.Primary => "Primärgruppe",
        _ => "?",
    };

    /// <summary>
    /// Zieht den ersten RDN-Wert aus einem DN — aus
    /// <c>CN=Müller\, Hans,OU=Benutzer,DC=…</c> wird <c>Müller, Hans</c>.
    /// Wird für die Anzeige des Vorgesetzten gebraucht, wo AD nur den DN liefert.
    /// </summary>
    public static string? FriendlyNameFromDn(string? dn)
    {
        if (string.IsNullOrWhiteSpace(dn)) return null;

        int eq = dn.IndexOf('=');
        if (eq < 0) return dn;

        var sb = new System.Text.StringBuilder(dn.Length);
        for (int i = eq + 1; i < dn.Length; i++)
        {
            char c = dn[i];
            if (c == '\\' && i + 1 < dn.Length)
            {
                // Escaptes Zeichen im RDN (typisch das Komma in "Nachname\, Vorname")
                sb.Append(dn[++i]);
                continue;
            }
            if (c == ',') break;
            sb.Append(c);
        }
        return sb.Length > 0 ? sb.ToString() : dn;
    }

    /// <summary>
    /// Baut aus einem DN den Organisationspfad in Lesereihenfolge:
    /// <c>lhp.intern / Benutzer / FB 5424</c>. Der DN selbst liest sich von
    /// innen nach außen und ist für einen schnellen Blick unbrauchbar.
    /// </summary>
    public static string? OrganizationalPath(string? dn)
    {
        if (string.IsNullOrWhiteSpace(dn)) return null;

        var containers = new List<string>();
        var domain = new List<string>();

        foreach (string part in SplitDn(dn).Skip(1))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            string type = part[..eq].Trim();
            string value = part[(eq + 1)..].Replace("\\,", ",");

            if (type.Equals("DC", StringComparison.OrdinalIgnoreCase)) domain.Add(value);
            else containers.Insert(0, value);
        }

        string domainName = string.Join('.', domain);
        return containers.Count == 0
            ? (domainName.Length > 0 ? domainName : null)
            : $"{domainName} / {string.Join(" / ", containers)}";
    }

    /// <summary>
    /// Zerlegt einen DN an den nicht escapten Kommas. <c>string.Split(',')</c>
    /// geht hier schief, sobald ein Nachname ein Komma enthält — und genau das
    /// ist in vielen Domänen die Standard-Namenskonvention.
    /// </summary>
    public static IReadOnlyList<string> SplitDn(string dn)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        for (int i = 0; i < dn.Length; i++)
        {
            char c = dn[i];
            if (c == '\\' && i + 1 < dn.Length)
            {
                current.Append(c).Append(dn[++i]);
                continue;
            }
            if (c == ',')
            {
                parts.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) parts.Add(current.ToString());
        return parts;
    }
}
