namespace LDAPeek.Services;

/// <summary>
/// LDAP-Attributnamen als Konstanten. Tippfehler in Attributnamen sind sonst
/// stille Fehler: der Server liefert das Attribut einfach nicht zurück, und im
/// UI bleibt ein Feld leer, ohne dass irgendwo etwas protokolliert wird.
/// </summary>
internal static class AdAttributes
{
    // Identität
    public const string SamAccountName = "sAMAccountName";
    public const string UserPrincipalName = "userPrincipalName";
    public const string DistinguishedName = "distinguishedName";
    public const string DisplayName = "displayName";
    public const string CommonName = "cn";
    public const string GivenName = "givenName";
    public const string Surname = "sn";
    public const string Description = "description";
    public const string ObjectSid = "objectSid";
    public const string ObjectGuid = "objectGUID";
    public const string ObjectClass = "objectClass";

    // Kontakt
    public const string Mail = "mail";
    public const string TelephoneNumber = "telephoneNumber";
    public const string Mobile = "mobile";
    public const string IpPhone = "ipPhone";
    public const string Facsimile = "facsimileTelephoneNumber";

    // Organisation
    public const string Title = "title";
    public const string Department = "department";
    public const string Company = "company";
    public const string Office = "physicalDeliveryOfficeName";
    public const string Manager = "manager";
    public const string EmployeeId = "employeeID";
    public const string EmployeeType = "employeeType";
    public const string StreetAddress = "streetAddress";
    public const string City = "l";
    public const string PostalCode = "postalCode";

    // Konto
    public const string UserAccountControl = "userAccountControl";
    public const string PwdLastSet = "pwdLastSet";
    public const string LastLogonTimestamp = "lastLogonTimestamp";
    public const string AccountExpires = "accountExpires";
    public const string LockoutTime = "lockoutTime";
    public const string BadPwdCount = "badPwdCount";
    public const string LogonCount = "logonCount";
    public const string WhenCreated = "whenCreated";
    public const string WhenChanged = "whenChanged";
    public const string PasswordExpiryComputed = "msDS-UserPasswordExpiryTimeComputed";

    // Profil
    public const string HomeDirectory = "homeDirectory";
    public const string HomeDrive = "homeDrive";
    public const string ScriptPath = "scriptPath";
    public const string ProfilePath = "profilePath";
    public const string ThumbnailPhoto = "thumbnailPhoto";

    // Gruppen
    public const string MemberOf = "memberOf";
    public const string PrimaryGroupId = "primaryGroupID";
    public const string GroupType = "groupType";
    public const string ManagedBy = "managedBy";
    public const string Member = "member";

    // RootDSE
    public const string DefaultNamingContext = "defaultNamingContext";
    public const string ConfigurationNamingContext = "configurationNamingContext";
    public const string DnsHostName = "dnsHostName";
    public const string DomainControllerFunctionality = "domainControllerFunctionality";

    /// <summary>Attribute, die für einen Treffer in der Ergebnisliste reichen.</summary>
    public static readonly string[] SearchResultSet =
    [
        SamAccountName, DisplayName, CommonName, Mail, DistinguishedName,
        UserAccountControl, Department, Title,
    ];

    /// <summary>Vollständiges Attributset für die Detailansicht eines Benutzers.</summary>
    public static readonly string[] UserDetailSet =
    [
        SamAccountName, UserPrincipalName, DistinguishedName, DisplayName, CommonName,
        GivenName, Surname, Description, ObjectSid, ObjectGuid,
        Mail, TelephoneNumber, Mobile, IpPhone, Facsimile,
        Title, Department, Company, Office, Manager, EmployeeId, EmployeeType,
        StreetAddress, City, PostalCode,
        UserAccountControl, PwdLastSet, LastLogonTimestamp, AccountExpires, LockoutTime,
        BadPwdCount, LogonCount, WhenCreated, WhenChanged, PasswordExpiryComputed,
        HomeDirectory, HomeDrive, ScriptPath, ProfilePath, ThumbnailPhoto,
        PrimaryGroupId, MemberOf,
    ];

    /// <summary>
    /// Attributset für eine Gruppe. <c>memberOf</c> ist hier kein Beiwerk: Es
    /// liefert die Kanten für den Verschachtelungsbaum, und zwar innerhalb der
    /// Abfrage, die ohnehin läuft — siehe <see cref="GroupTree"/>.
    /// </summary>
    public static readonly string[] GroupSet =
    [
        CommonName, SamAccountName, DistinguishedName, Description,
        GroupType, ObjectSid, Mail, ManagedBy, WhenCreated, MemberOf,
    ];
}
