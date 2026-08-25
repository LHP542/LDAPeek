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
