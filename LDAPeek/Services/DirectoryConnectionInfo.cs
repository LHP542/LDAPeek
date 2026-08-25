namespace LDAPeek.Services;

/// <summary>
/// Womit LDAPeek gerade verbunden ist. Wandert in die Statusleiste, damit bei
/// einer überraschenden Auskunft sofort sichtbar ist, welche Domäne und welcher
/// Domain Controller sie geliefert haben — bei mehreren Domänen oder einem
/// falsch gesetzten Server ist genau das die Erklärung.
/// </summary>
public sealed class DirectoryConnectionInfo
{
    /// <summary>Angesprochener Server bzw. Domänenname, wie konfiguriert.</summary>
    public required string Target { get; init; }

    /// <summary>Der DC, der tatsächlich geantwortet hat (aus dem RootDSE).</summary>
    public string? DomainController { get; init; }

    /// <summary>Basis-DN, unter dem gesucht wird.</summary>
    public required string SearchBase { get; init; }

    /// <summary>Konto, mit dem gebunden wurde.</summary>
    public required string BoundAs { get; init; }

    /// <summary>Läuft die Verbindung über LDAPS?</summary>
    public bool IsTls { get; init; }

    /// <summary>Kurzfassung für die Statusleiste.</summary>
    public string StatusText
    {
        get
        {
            string dc = DomainController ?? Target;
            string protocol = IsTls ? "LDAPS" : "LDAP";
            return $"{protocol} · {dc} · angemeldet als {BoundAs}";
        }
    }
}
