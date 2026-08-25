using System.Text;

namespace LDAPeek.Services;

/// <summary>
/// Baut LDAP-Suchfilter und kümmert sich um das Escaping nach RFC 4515.
///
/// Warum eine eigene Klasse: Benutzereingaben landen direkt im Filter. Ein
/// ungeprüftes <c>)</c> oder <c>*</c> im Suchfeld macht aus dem Filter im besten
/// Fall Syntaxmüll (Exception) und im schlechtesten einen Filter, der etwas
/// anderes sucht als gemeint. Das ist die LDAP-Variante von SQL-Injection —
/// hier zwar nur lesend, aber genauso vermeidbar.
/// </summary>
internal static class LdapFilter
{
    /// <summary>
    /// Escaped einen Wert für die Verwendung in einem LDAP-Filter (RFC 4515,
    /// Abschnitt 3). Die fünf Sonderzeichen werden als <c>\XX</c> kodiert.
    /// </summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var sb = new StringBuilder(value.Length + 8);
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\5c"); break;
                case '*': sb.Append("\\2a"); break;
                case '(': sb.Append("\\28"); break;
                case ')': sb.Append("\\29"); break;
                case '\0': sb.Append("\\00"); break;
                case '/': sb.Append("\\2f"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Escaped ein Byte-Array (objectSid, objectGUID) für einen Filtervergleich.
    /// Jedes Byte wird zu <c>\XX</c> — anders lässt sich Binärvergleich in einem
    /// Textfilter nicht ausdrücken.
    /// </summary>
    public static string EscapeBinary(ReadOnlySpan<byte> value)
    {
        var sb = new StringBuilder(value.Length * 3);
        foreach (byte b in value)
        {
            sb.Append('\\').Append(b.ToString("x2"));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Baut den Filter für die Benutzersuche über mehrere Attribute.
    /// </summary>
    /// <param name="query">Rohe Nutzereingabe (wird escaped).</param>
    /// <param name="exact">
    /// true = exakter Vergleich, false = Teilstring-Suche mit <c>*q*</c>.
    /// </param>
    /// <remarks>
    /// <c>objectCategory=person</c> statt nur <c>objectClass=user</c>: sonst
    /// kommen auch Computerkonten zurück (<c>computer</c> erbt von <c>user</c>) —
    /// ein Klassiker, der in jeder Domäne für „warum steht da ein Server?" sorgt.
    /// </remarks>
    public static string UserSearch(string query, bool exact = false)
    {
        string q = Escape(query.Trim());
        string pattern = exact ? q : $"*{q}*";

        var sb = new StringBuilder();
        sb.Append("(&(objectCategory=person)(objectClass=user)(|");
        foreach (string attr in SearchAttributes)
        {
            sb.Append('(').Append(attr).Append('=').Append(pattern).Append(')');
        }
        // employeeID wird fast immer exakt gesucht — ein *123* über alle
        // Personalnummern liefert sonst reihenweise Zufallstreffer.
        sb.Append("(employeeID=").Append(q).Append(')');
        sb.Append("))");
        return sb.ToString();
    }

    private static readonly string[] SearchAttributes =
    [
        AdAttributes.SamAccountName,
        AdAttributes.DisplayName,
        AdAttributes.CommonName,
        AdAttributes.GivenName,
        AdAttributes.Surname,
        AdAttributes.Mail,
        AdAttributes.UserPrincipalName,
    ];

    /// <summary>Filter auf genau ein Benutzerkonto per sAMAccountName.</summary>
    public static string UserBySamAccountName(string sam) =>
        $"(&(objectCategory=person)(objectClass=user)(sAMAccountName={Escape(sam)}))";

    /// <summary>Filter auf genau ein Objekt per objectSid (binär).</summary>
    public static string BySid(ReadOnlySpan<byte> sid) =>
        $"(objectSid={EscapeBinary(sid)})";

    /// <summary>
    /// Filter für alle Gruppen, in denen <paramref name="userDn"/> Mitglied ist —
    /// <b>inklusive verschachtelter</b>. Nutzt LDAP_MATCHING_RULE_IN_CHAIN
    /// (OID 1.2.840.113556.1.4.1941), das der Domain Controller serverseitig
    /// auflöst. Eine einzige Abfrage statt rekursivem Nachladen pro Gruppe.
    ///
    /// Nicht enthalten: die <b>Primärgruppe</b> (typisch „Domänen-Benutzer").
    /// Die steht nirgends im <c>member</c>-Attribut, sondern ergibt sich aus
    /// <c>primaryGroupID</c> — siehe <see cref="SidUtil.BuildGroupSid"/>.
    /// </summary>
    public static string GroupsOfMemberRecursive(string userDn) =>
        $"(&(objectCategory=group)(member:1.2.840.113556.1.4.1941:={Escape(userDn)}))";

    /// <summary>Filter auf ein Objekt per Distinguished Name (Base-Suche).</summary>
    public static string AnyObject() => "(objectClass=*)";
}
