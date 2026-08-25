namespace LDAPeek.Models;

/// <summary>
/// Ein Treffer in der Ergebnisliste. Bewusst schmal gehalten: die Suche holt nur
/// die Attribute, die in der Liste sichtbar sind. Die vollständigen Daten kommen
/// erst beim Anklicken über eine zweite, gezielte Abfrage
/// (<see cref="Services.IDirectoryService.LoadUserAsync"/>).
///
/// Der Grund ist nicht Sparsamkeit um ihrer selbst willen: eine Suche nach
/// „mueller" kann 200 Treffer haben, und für jeden davon Foto, Gruppen und
/// Zeitstempel mitzuziehen macht aus einer halben Sekunde mehrere.
/// </summary>
public sealed class UserSearchHit
{
    public required string DistinguishedName { get; init; }
    public string? SamAccountName { get; init; }
    public string? DisplayName { get; init; }
    public string? CommonName { get; init; }
    public string? Mail { get; init; }
    public string? Department { get; init; }
    public string? Title { get; init; }
    public AccountFlags Flags { get; init; }

    public bool IsDisabled => Flags.HasFlag(AccountFlags.Disabled);

    public string BestName => DisplayName ?? CommonName ?? SamAccountName ?? DistinguishedName;

    /// <summary>Zweite Zeile im Listeneintrag: Anmeldename und, falls vorhanden, Abteilung.</summary>
    public string SubtitleText
    {
        get
        {
            string login = SamAccountName ?? "—";
            return string.IsNullOrWhiteSpace(Department) ? login : $"{login} · {Department}";
        }
    }
}
