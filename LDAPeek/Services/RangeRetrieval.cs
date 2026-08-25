namespace LDAPeek.Services;

/// <summary>
/// Hilfsfunktionen für das LDAP-Range-Retrieval (MS-ADTS 3.1.1.3.1.3.2).
///
/// <b>Warum das nötig ist:</b> Ein Domain Controller liefert mehrwertige
/// Attribute nur bis zu <c>MaxValRange</c> (Standard 1500) auf einmal. Fragt man
/// <c>memberOf</c> eines Benutzers ab, der in 1600 Gruppen ist, kommt das
/// Attribut <b>nicht</b> als <c>memberOf</c> zurück, sondern als
/// <c>memberOf;range=0-1499</c> — und wer nur auf den Namen <c>memberOf</c>
/// prüft, bekommt schlicht <b>null Gruppen</b> zurück. Kein Fehler, keine
/// Warnung, einfach eine leere Liste.
///
/// Das ist die klassische Falle, die erst beim ersten Konto mit vielen
/// Mitgliedschaften auffällt — typischerweise bei einem Dienstkonto oder einem
/// langjährigen Mitarbeiter, also genau dann, wenn jemand eine verlässliche
/// Auskunft braucht.
/// </summary>
internal static class RangeRetrieval
{
    private const string Marker = ";range=";

    /// <summary>
    /// Zerlegt einen zurückgelieferten Attributnamen.
    /// </summary>
    /// <param name="attributeName">z.B. <c>memberOf;range=0-1499</c> oder <c>memberOf</c>.</param>
    /// <param name="baseName">Der Attributname ohne Range-Zusatz.</param>
    /// <param name="lower">Index des ersten enthaltenen Werts.</param>
    /// <param name="upper">
    /// Index des letzten enthaltenen Werts, oder <c>null</c> bei <c>*</c> —
    /// dann ist die Liste vollständig und es folgt keine weitere Abfrage.
    /// </param>
    /// <returns>true, wenn der Name einen Range-Zusatz trug.</returns>
    public static bool TryParse(string attributeName, out string baseName, out int lower, out int? upper)
    {
        baseName = attributeName;
        lower = 0;
        upper = null;

        int marker = attributeName.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return false;

        baseName = attributeName[..marker];
        string range = attributeName[(marker + Marker.Length)..];

        int dash = range.IndexOf('-');
        if (dash <= 0) return false;

        if (!int.TryParse(range[..dash], out lower)) return false;

        string upperPart = range[(dash + 1)..];
        if (upperPart == "*")
        {
            upper = null;
            return true;
        }
        if (!int.TryParse(upperPart, out int parsedUpper)) return false;

        upper = parsedUpper;
        return true;
    }

    /// <summary>
    /// Baut den Attributnamen für die nächste Runde:
    /// nach <c>memberOf;range=0-1499</c> folgt <c>memberOf;range=1500-*</c>.
    /// </summary>
    public static string NextRequest(string baseName, int nextLower) =>
        $"{baseName}{Marker}{nextLower}-*";

    /// <summary>Der Einstieg: alles ab 0 anfordern.</summary>
    public static string FirstRequest(string baseName) => NextRequest(baseName, 0);

    /// <summary>
    /// Ist die Liste mit dieser Antwort vollständig? Das ist genau dann der
    /// Fall, wenn die Obergrenze <c>*</c> war.
    /// </summary>
    public static bool IsComplete(int? upper) => upper is null;
}
