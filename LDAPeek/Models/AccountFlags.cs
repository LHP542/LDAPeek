namespace LDAPeek.Models;

/// <summary>
/// Bitmaske des AD-Attributs <c>userAccountControl</c>. Nur die Flags, die für
/// ein Nachschlage-Werkzeug interessant sind — die vollständige Liste steht in
/// MS-ADTS 2.2.16.
/// </summary>
[Flags]
public enum AccountFlags
{
    None = 0,

    /// <summary>Konto ist deaktiviert.</summary>
    Disabled = 0x0002,

    /// <summary>Konto darf ohne Passwort angelegt/betrieben werden.</summary>
    PasswordNotRequired = 0x0020,

    /// <summary>Benutzer darf sein Passwort nicht selbst ändern.</summary>
    PasswordCantChange = 0x0040,

    /// <summary>Normales Benutzerkonto (im Gegensatz zu Computer/Trust).</summary>
    NormalAccount = 0x0200,

    /// <summary>Passwort läuft nie ab.</summary>
    DontExpirePassword = 0x10000,

    /// <summary>Anmeldung nur per Smartcard.</summary>
    SmartcardRequired = 0x40000,

    /// <summary>Konto darf für Delegierung verwendet werden (Kerberos).</summary>
    TrustedForDelegation = 0x80000,

    /// <summary>Konto ist gegen Delegierung geschützt („sensitiv").</summary>
    NotDelegated = 0x100000,

    /// <summary>Nur DES-Verschlüsselung (Altlast, sicherheitsrelevant).</summary>
    UseDesKeyOnly = 0x200000,

    /// <summary>Kerberos-Vorauthentifizierung nicht erforderlich (AS-REP-Roasting).</summary>
    DontRequirePreauth = 0x400000,

    /// <summary>Passwort ist abgelaufen (berechnetes Flag).</summary>
    PasswordExpired = 0x800000,
}
