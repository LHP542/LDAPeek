#if DEBUG
using System.Runtime.Versioning;
using LDAPeek.Models;

namespace LDAPeek.Services;

/// <summary>
/// Verzeichnis-Attrappe mit frei erfundenen Daten — <b>nur für die
/// Bildschirmfotos der Dokumentation</b>.
///
/// <b>Warum das sein muss:</b> Das Repository ist öffentlich. Ein Screenshot
/// mit echten Verzeichnisdaten würde Namen, Telefonnummern, E-Mail-Adressen und
/// Gruppenmitgliedschaften realer Personen veröffentlichen. Für einen
/// öffentlichen Auftraggeber ist das nicht verhandelbar, und ein „ich schwärze
/// das nachher" hält der ersten Eile nicht stand.
///
/// Steht bewusst unter <c>#if DEBUG</c>: Im ausgelieferten Release-Binary
/// existiert diese Klasse nicht.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DemoDirectoryService : IDirectoryService
{
    private const string Base = "OU=Benutzer,DC=musterstadt,DC=beispiel";

    public DirectoryConnectionInfo? ConnectionInfo { get; private set; }

    public Task<DirectoryConnectionInfo> ConnectAsync(CancellationToken cancellationToken)
    {
        ConnectionInfo = new DirectoryConnectionInfo
        {
            Target = "musterstadt.beispiel",
            DomainController = "DC01.musterstadt.beispiel",
            SearchBase = "DC=musterstadt,DC=beispiel",
            BoundAs = @"MUSTERSTADT\m.mustermann",
        };
        return Task.FromResult(ConnectionInfo);
    }

    public Task<IReadOnlyList<UserSearchHit>> SearchUsersAsync(
        string query, CancellationToken cancellationToken)
    {
        IReadOnlyList<UserSearchHit> hits =
        [
            new()
            {
                DistinguishedName = $"CN=Mustermann\\, Maxi,{Base}",
                SamAccountName = "m.mustermann",
                DisplayName = "Mustermann, Maxi",
                Mail = "maxi.mustermann@musterstadt.beispiel",
                Department = "FB 42 — Beispielwesen",
                Title = "Sachbearbeitung",
            },
            new()
            {
                DistinguishedName = $"CN=Musterfrau\\, Erika,{Base}",
                SamAccountName = "e.musterfrau",
                DisplayName = "Musterfrau, Erika",
                Mail = "erika.musterfrau@musterstadt.beispiel",
                Department = "FB 42 — Beispielwesen",
            },
            new()
            {
                DistinguishedName = $"CN=Mustermann\\, Moritz,{Base}",
                SamAccountName = "mo.mustermann",
                DisplayName = "Mustermann, Moritz (ausgeschieden)",
                Department = "FB 17 — Ehemalige",
                Flags = AccountFlags.NormalAccount | AccountFlags.Disabled,
            },
        ];
        return Task.FromResult(hits);
    }

    public Task<AdUser?> LoadUserAsync(string distinguishedName, CancellationToken cancellationToken)
    {
        AdUser? user = new()
        {
            DistinguishedName = $"CN=Mustermann\\, Maxi,{Base}",
            SamAccountName = "m.mustermann",
            UserPrincipalName = "m.mustermann@musterstadt.beispiel",
            DisplayName = "Mustermann, Maxi",
            GivenName = "Maxi",
            Surname = "Mustermann",
            Description = "Sachbearbeitung Beispielwesen",
            Mail = "maxi.mustermann@musterstadt.beispiel",
            TelephoneNumber = "+49 331 000-4242",
            Mobile = "+49 151 00000000",
            IpPhone = "4242",
            Title = "Sachbearbeitung",
            Department = "FB 42 — Beispielwesen",
            Company = "Musterstadt",
            Office = "Rathaus, Zimmer 2.14",
            ManagerDn = $"CN=Musterfrau\\, Erika,{Base}",
            EmployeeId = "100042",
            Street = "Musterplatz 1",
            PostalCode = "14400",
            City = "Musterstadt",
            Flags = AccountFlags.NormalAccount,
            PasswordLastSet = DateTimeOffset.Now.AddDays(-73),
            PasswordExpires = DateTimeOffset.Now.AddDays(17),
            LastLogon = DateTimeOffset.Now.AddHours(-3),
            BadPasswordCount = 0,
            LogonCount = 1284,
            Created = new DateTimeOffset(2019, 4, 1, 8, 0, 0, TimeSpan.Zero),
            Changed = DateTimeOffset.Now.AddDays(-12),
            HomeDirectory = @"\\dateiserver\home\m.mustermann",
            HomeDrive = "H:",
            LogonScript = "anmeldung.cmd",
            Sid = "S-1-5-21-1004336348-1177238915-682003330-4242",
            ObjectGuid = "8f14e45f-ceea-467a-9f7c-6c0e1a2b3c4d",
            PrimaryGroupId = 513,
            MemberOfDns = [],
        };
        return Task.FromResult<AdUser?>(user);
    }

    public Task<IReadOnlyList<AdGroup>> LoadGroupsAsync(
        AdUser user, bool includeNested, CancellationToken cancellationToken)
    {
        IReadOnlyList<AdGroup> groups =
        [
            Group("Domänen-Benutzer", MembershipKind.Primary, GroupScope.Global,
                "Standardgruppe aller Konten"),
            Group("RG-FS-FB42-Ablage-Schreiben", MembershipKind.Direct, GroupScope.DomainLocal,
                "Schreibzugriff auf die Fachbereichsablage"),
            Group("RG-Drucker-Rathaus-2OG", MembershipKind.Direct, GroupScope.Global,
                "Druckerfreigabe 2. Obergeschoss"),
            Group("AP-FB42", MembershipKind.Direct, GroupScope.Global,
                "Arbeitsplatzprofil Fachbereich 42"),
            Group("RG-Fachverfahren-Beispiel-Bearbeiten", MembershipKind.Nested, GroupScope.Global,
                "über AP-FB42 geerbt"),
            Group("RG-FS-FB42-Vorlagen-Lesen", MembershipKind.Nested, GroupScope.DomainLocal,
                "über AP-FB42 geerbt"),
            Group("RG-Intranet-Redaktion", MembershipKind.Nested, GroupScope.Universal,
                "über RG-Fachverfahren-Beispiel-Bearbeiten geerbt"),
            Group("Verteiler-FB42-Alle", MembershipKind.Nested, GroupScope.Universal,
                "E-Mail-Verteiler", GroupKind.Distribution),
        ];

        if (!includeNested)
        {
            groups = [.. groups.Where(g => g.Membership != MembershipKind.Nested)];
        }
        return Task.FromResult(groups);
    }

    private static AdGroup Group(
        string name, MembershipKind membership, GroupScope scope, string description,
        GroupKind kind = GroupKind.Security) => new()
        {
            DistinguishedName = $"CN={name},OU=Gruppen,DC=musterstadt,DC=beispiel",
            Name = name,
            SamAccountName = name,
            Description = description,
            Scope = scope,
            Kind = kind,
            Membership = membership,
        };

    public Task<bool> IsMemberOfGroupAsync(
        string samAccountName, byte[] groupSid, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public void Invalidate() => ConnectionInfo = null;
}
#endif
