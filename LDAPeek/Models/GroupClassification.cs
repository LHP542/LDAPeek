namespace LDAPeek.Models;

/// <summary>Gültigkeitsbereich einer AD-Gruppe (aus <c>groupType</c>).</summary>
public enum GroupScope
{
    Unknown,

    /// <summary>Lokal in der Domäne — kann Mitglieder aus der ganzen Gesamtstruktur haben.</summary>
    DomainLocal,

    /// <summary>Global — Mitglieder nur aus der eigenen Domäne, überall verwendbar.</summary>
    Global,

    /// <summary>Universell — gesamtstrukturweit, liegt im Globalen Katalog.</summary>
    Universal,

    /// <summary>Vordefinierte lokale Gruppe (Builtin-Container).</summary>
    BuiltinLocal,
}

/// <summary>Art einer AD-Gruppe (aus <c>groupType</c>).</summary>
public enum GroupKind
{
    /// <summary>Verteilergruppe — nur E-Mail, keine Berechtigungen.</summary>
    Distribution,

    /// <summary>Sicherheitsgruppe — trägt Berechtigungen.</summary>
    Security,
}

/// <summary>Wie ein Benutzer zu einer Gruppe gekommen ist.</summary>
public enum MembershipKind
{
    /// <summary>Steht direkt im <c>memberOf</c> des Benutzers.</summary>
    Direct,

    /// <summary>Geerbt über eine andere Gruppe (Verschachtelung).</summary>
    Nested,

    /// <summary>Primärgruppe des Kontos (<c>primaryGroupID</c>).</summary>
    Primary,
}
