using LDAPeek.Services;

namespace LDAPeek.Models;

/// <summary>
/// Eine AD-Gruppe samt der Information, <b>wie</b> der gerade angezeigte
/// Benutzer hineingekommen ist. Genau diese Herkunft ist der Grund, warum ein
/// Werkzeug wie dieses existiert: „Warum hat der Kollege Zugriff auf X?" lässt
/// sich mit einer flachen Gruppenliste nicht beantworten.
/// </summary>
public sealed class AdGroup
{
    public required string DistinguishedName { get; init; }
    public string? Name { get; init; }
    public string? SamAccountName { get; init; }
    public string? Description { get; init; }
    public string? Mail { get; init; }
    public string? ManagedBy { get; init; }
    public string? Sid { get; init; }
    public DateTimeOffset? Created { get; init; }

    public GroupScope Scope { get; init; }
    public GroupKind Kind { get; init; }
    public MembershipKind Membership { get; set; }

    /// <summary>
    /// Die Gruppen, in denen <b>diese Gruppe</b> Mitglied ist. Daraus entsteht
    /// der Verschachtelungsbaum, und zwar ohne eine einzige zusätzliche
    /// Abfrage: <c>LDAP_MATCHING_RULE_IN_CHAIN</c> liefert die vollständige
    /// Kette, also ist jede Zwischengruppe ohnehin im Ergebnis. Ihre
    /// <c>memberOf</c>-Werte sind damit genau die Kanten des Baums.
    /// </summary>
    public IReadOnlyList<string> MemberOfDns { get; init; } = [];

    /// <summary>Anzeigename mit Rückfall auf DN, falls die Gruppe kein cn geliefert hat.</summary>
    public string DisplayName =>
        Name ?? SamAccountName ?? AdValue.FriendlyNameFromDn(DistinguishedName) ?? DistinguishedName;

    public string ScopeText => AdValue.DescribeScope(Scope);
    public string KindText => AdValue.DescribeKind(Kind);
    public string MembershipText => AdValue.DescribeMembership(Membership);

    /// <summary>Organisationspfad der Gruppe, z.B. <c>lhp.intern / Gruppen / Fachbereich 54</c>.</summary>
    public string? Path => AdValue.OrganizationalPath(DistinguishedName);

    /// <summary>
    /// Sortierschlüssel: Primärgruppe zuerst, dann direkte, dann verschachtelte —
    /// innerhalb der Gruppe alphabetisch. So steht oben, was der Benutzer selbst
    /// zugewiesen bekommen hat, und darunter, was daraus folgt.
    /// </summary>
    public int MembershipOrder => Membership switch
    {
        MembershipKind.Primary => 0,
        MembershipKind.Direct => 1,
        _ => 2,
    };

    public bool IsDirect => Membership == MembershipKind.Direct;
    public bool IsNested => Membership == MembershipKind.Nested;
    public bool IsPrimary => Membership == MembershipKind.Primary;
    public bool IsSecurity => Kind == GroupKind.Security;

    /// <summary>Zweite Zeile im Listeneintrag: Beschreibung, ersatzweise der Pfad.</summary>
    public string SubtitleText => Description ?? Path ?? DistinguishedName;

    /// <summary>Zusammenfassung von Art und Bereich für den Eintrag.</summary>
    public string TypeText => $"{KindText} · {ScopeText}";

    // ------------------------------------------------------------------
    // Abzeichen in der Liste. Der Grundsatz: zeigen, was von der Regel
    // abweicht. Bei 130 Zeilen, in denen dreimal dasselbe steht, liest man
    // keine davon mehr — und übersieht die eine, die anders ist.
    // ------------------------------------------------------------------

    /// <summary>
    /// Herkunft nur bei „direkt" und „Primärgruppe" zeigen. In einer
    /// aufgelösten Liste ist „verschachtelt" der Normalfall, und im Baum sagt
    /// die Einrückung es ohnehin.
    /// </summary>
    public bool ShowMembershipBadge => Membership != MembershipKind.Nested;

    /// <summary>
    /// Art und Bereich nur zeigen, wenn sie vom Üblichen abweichen: Eine
    /// globale Sicherheitsgruppe ist der Regelfall und braucht kein Etikett.
    /// Eine Verteilergruppe dagegen trägt keine Rechte — das ist die
    /// Information, auf die es ankommt.
    /// </summary>
    public bool ShowTypeBadge => Kind != GroupKind.Security || Scope != GroupScope.Global;

    /// <summary>Kurzform von <see cref="TypeText"/>: nennt nur das Abweichende.</summary>
    public string TypeBadgeText
    {
        get
        {
            var parts = new List<string>(2);
            if (Kind != GroupKind.Security) parts.Add(KindText);
            if (Scope != GroupScope.Global) parts.Add(ScopeText);
            return parts.Count == 0 ? TypeText : string.Join(" · ", parts);
        }
    }

    /// <summary>Volltext für den Tooltip — dort darf alles stehen.</summary>
    public string TooltipText => string.Join('\n',
        new[] { DisplayName, Description, $"{MembershipText} · {TypeText}", DistinguishedName }
            .Where(line => !string.IsNullOrWhiteSpace(line)));

    /// <summary>Ein Zeile für den Zwischenablage-/CSV-Export.</summary>
    public string ToCsvRow() => string.Join(';',
    [
        Csv(DisplayName), Csv(SamAccountName), Csv(KindText), Csv(ScopeText),
        Csv(MembershipText), Csv(Description), Csv(DistinguishedName),
    ]);

    /// <summary>CSV-Kopfzeile passend zu <see cref="ToCsvRow"/>.</summary>
    public const string CsvHeader = "Gruppe;Anmeldename;Art;Bereich;Herkunft;Beschreibung;DN";

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        // Semikolon als Trenner, weil Excel im deutschen Gebietsschema danach
        // trennt. Werte mit Semikolon/Anführungszeichen/Umbruch werden gequotet.
        bool needsQuotes = value.Contains(';') || value.Contains('"')
            || value.Contains('\n') || value.Contains('\r');
        return needsQuotes ? '"' + value.Replace("\"", "\"\"") + '"' : value;
    }
}
