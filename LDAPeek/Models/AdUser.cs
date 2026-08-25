using LDAPeek.Services;

namespace LDAPeek.Models;

/// <summary>
/// Ein AD-Benutzerkonto mit allen Attributen, die LDAPeek anzeigt.
/// Rein lesend — LDAPeek schreibt grundsätzlich nichts ins Verzeichnis.
/// </summary>
public sealed class AdUser
{
    public required string DistinguishedName { get; init; }

    // Identität
    public string? SamAccountName { get; init; }
    public string? UserPrincipalName { get; init; }
    public string? DisplayName { get; init; }
    public string? CommonName { get; init; }
    public string? GivenName { get; init; }
    public string? Surname { get; init; }
    public string? Description { get; init; }
    public string? Sid { get; init; }
    public string? ObjectGuid { get; init; }

    // Kontakt
    public string? Mail { get; init; }
    public string? TelephoneNumber { get; init; }
    public string? Mobile { get; init; }
    public string? IpPhone { get; init; }
    public string? Fax { get; init; }

    // Organisation
    public string? Title { get; init; }
    public string? Department { get; init; }
    public string? Company { get; init; }
    public string? Office { get; init; }
    public string? ManagerDn { get; init; }
    public string? EmployeeId { get; init; }
    public string? EmployeeType { get; init; }
    public string? Street { get; init; }
    public string? City { get; init; }
    public string? PostalCode { get; init; }

    // Konto
    public AccountFlags Flags { get; init; }
    public DateTimeOffset? PasswordLastSet { get; init; }
    public bool MustChangePassword { get; init; }
    public DateTimeOffset? PasswordExpires { get; init; }
    public DateTimeOffset? LastLogon { get; init; }
    public DateTimeOffset? AccountExpires { get; init; }
    public DateTimeOffset? LockedSince { get; init; }
    public int BadPasswordCount { get; init; }
    public int LogonCount { get; init; }
    public DateTimeOffset? Created { get; init; }
    public DateTimeOffset? Changed { get; init; }

    // Profil
    public string? HomeDirectory { get; init; }
    public string? HomeDrive { get; init; }
    public string? LogonScript { get; init; }
    public string? ProfilePath { get; init; }
    public byte[]? Photo { get; init; }

    // Gruppen
    public int PrimaryGroupId { get; init; }
    public IReadOnlyList<string> MemberOfDns { get; init; } = [];

    /// <summary>Anzeigename mit Rückfall über cn und Anmeldename.</summary>
    public string BestName =>
        DisplayName ?? CommonName ?? SamAccountName ?? DistinguishedName;

    public bool IsDisabled => Flags.HasFlag(AccountFlags.Disabled);

    /// <summary>
    /// AD meldet nicht „gesperrt: ja/nein", sondern nur den Zeitpunkt der
    /// Sperrung. Ein gesetzter Zeitpunkt heißt gesperrt.
    /// </summary>
    public bool IsLockedOut => LockedSince is not null;

    public bool PasswordNeverExpires => Flags.HasFlag(AccountFlags.DontExpirePassword);

    /// <summary>Ist das Konto zeitlich abgelaufen?</summary>
    public bool IsExpired => AccountExpires is { } expires && expires < DateTimeOffset.Now;

    /// <summary>Kurzer Zustandssatz für die Statuszeile im UI.</summary>
    public string StatusText
    {
        get
        {
            if (IsDisabled) return "Deaktiviert";
            if (IsLockedOut) return "Gesperrt";
            if (IsExpired) return "Abgelaufen";
            return "Aktiv";
        }
    }

    /// <summary>Klartextliste der gesetzten Konto-Flags.</summary>
    public IReadOnlyList<string> FlagDescriptions => AdValue.DescribeAccountFlags(Flags);

    /// <summary>Vorgesetzter als lesbarer Name statt als DN.</summary>
    public string? ManagerName => AdValue.FriendlyNameFromDn(ManagerDn);

    /// <summary>Organisationspfad des Kontos.</summary>
    public string? Path => AdValue.OrganizationalPath(DistinguishedName);

    // ------------------------------------------------------------------
    // Anzeigetexte. Als Properties statt als Konverter im XAML, damit ein
    // leerer Wert überall gleich aussieht und das Fenster lesbar bleibt.
    // ------------------------------------------------------------------

    public string SamAccountNameText => Display.Text(SamAccountName);
    public string UserPrincipalNameText => Display.Text(UserPrincipalName);
    public string DescriptionText => Display.Text(Description);
    public string MailText => Display.Text(Mail);
    public string TelephoneText => Display.Text(TelephoneNumber);
    public string MobileText => Display.Text(Mobile);
    public string IpPhoneText => Display.Text(IpPhone);
    public string TitleText => Display.Text(Title);
    public string DepartmentText => Display.Text(Department);
    public string CompanyText => Display.Text(Company);
    public string OfficeText => Display.Text(Office);
    public string ManagerText => Display.Text(ManagerName);
    public string EmployeeIdText => Display.Text(EmployeeId);
    public string PathText => Display.Text(Path);
    public string SidText => Display.Text(Sid);
    public string ObjectGuidText => Display.Text(ObjectGuid);
    public string HomeDirectoryText => Display.Text(HomeDirectory);
    public string LogonScriptText => Display.Text(LogonScript);
    public string ProfilePathText => Display.Text(ProfilePath);

    public string AddressText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Street)) parts.Add(Street);
            string place = string.Join(' ',
                new[] { PostalCode, City }.Where(p => !string.IsNullOrWhiteSpace(p)));
            if (place.Length > 0) parts.Add(place);
            return parts.Count == 0 ? Display.Empty : string.Join(", ", parts);
        }
    }

    public string PasswordLastSetText => MustChangePassword
        ? "muss bei nächster Anmeldung geändert werden"
        : Display.DateWithAge(PasswordLastSet);

    public string PasswordExpiresText => PasswordNeverExpires
        ? "läuft nie ab"
        : Display.DateWithAge(PasswordExpires);

    public string LastLogonText => Display.DateWithAge(LastLogon);

    public string AccountExpiresText =>
        AccountExpires is null ? "läuft nie ab" : Display.DateWithAge(AccountExpires);

    public string LockedSinceText => Display.DateWithAge(LockedSince);
    public string CreatedText => Display.Date(Created);
    public string ChangedText => Display.DateWithAge(Changed);
    public string BadPasswordCountText => Display.Number(BadPasswordCount);
    public string LogonCountText => Display.Number(LogonCount);

    public string FlagsText => FlagDescriptions.Count == 0
        ? "keine Besonderheiten"
        : string.Join(" · ", FlagDescriptions);

    /// <summary>Für die Abzeichenfarbe: nur ein aktives Konto ist grün.</summary>
    public bool IsHealthy => !IsDisabled && !IsLockedOut && !IsExpired;

    /// <summary>Passwort läuft in den nächsten 14 Tagen ab — Anlass für einen Hinweis.</summary>
    public bool PasswordExpiresSoon =>
        !PasswordNeverExpires
        && PasswordExpires is { } expires
        && expires > DateTimeOffset.Now
        && expires < DateTimeOffset.Now.AddDays(14);
}
