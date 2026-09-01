using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;
using FluentAssertions;
using LDAPeek.Models;
using LDAPeek.Services;

namespace LDAPeek.Tests;

/// <summary>
/// Die Zugangsregel. Der wichtigste Fall ist der letzte: eine defekte oder
/// nicht auflösbare Regel darf NICHT zur Erlaubnis werden — sonst liesse sich
/// die Sperre durch Beschädigen einer Datei aushebeln, und dann wäre sie auch
/// als Hinweis wertlos.
/// </summary>
public class AccessPolicyTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "LDAPeek-Policy-" + Guid.NewGuid().ToString("N"));

    private string PolicyPath => Path.Combine(_dir, AccessPolicy.FileName);

    public AccessPolicyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* egal */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Ohne_Datei_gilt_keine_Einschraenkung()
    {
        // Der Normalfall fuer Entwicklung und fuer Installationen ohne Vorgabe.
        var policy = AccessPolicy.Load(PolicyPath);

        policy.IsActive.Should().BeFalse();
        policy.IsUnreadable.Should().BeFalse();
    }

    [Fact]
    public void Eine_leere_Gruppe_ist_keine_Einschraenkung()
    {
        File.WriteAllText(PolicyPath, """{ "RequiredGroup": "" }""");

        AccessPolicy.Load(PolicyPath).IsActive.Should().BeFalse();
    }

    [Fact]
    public void Gruppe_und_Hinweis_werden_gelesen()
    {
        File.WriteAllText(PolicyPath, """
            { "RequiredGroup": "RG-LDAPeek-Benutzer",
              "ContactHint": "Zugang beim Team IT-Basis-Dienste anfragen." }
            """);

        var policy = AccessPolicy.Load(PolicyPath);

        policy.IsActive.Should().BeTrue();
        policy.RequiredGroup.Should().Be("RG-LDAPeek-Benutzer");
        policy.ContactHint.Should().Contain("IT-Basis-Dienste");
    }

    [Fact]
    public void Eine_defekte_Datei_sperrt_statt_durchzulassen()
    {
        // Waere das anders, koennte jeder die Sperre durch Zerstoeren der Datei
        // umgehen — und die Regel waere selbst als Hinweis wertlos.
        File.WriteAllText(PolicyPath, "{ das ist kein JSON");

        var policy = AccessPolicy.Load(PolicyPath);

        policy.IsActive.Should().BeTrue();
        policy.IsUnreadable.Should().BeTrue();
        policy.ContactHint.Should().Contain("beschädigt");
    }

    [Fact]
    public void Ein_unbekanntes_Feld_verhindert_das_Laden_nicht()
    {
        File.WriteAllText(PolicyPath,
            """{ "RequiredGroup": "X", "NeuesFeldAusEinerSpaeterenVersion": 42 }""");

        AccessPolicy.Load(PolicyPath).RequiredGroup.Should().Be("X");
    }

    [Fact]
    public void DefaultPath_liegt_neben_der_Anwendung()
    {
        // Bewusst NICHT im Benutzerprofil: die Regel gehoert zur Auslieferung.
        AccessPolicy.DefaultPath()
            .Should().StartWith(AppContext.BaseDirectory)
            .And.EndWith(AccessPolicy.FileName);
    }

    [Fact]
    public void Die_Datei_ist_von_Hand_schreibbar()
    {
        // Beim Ausrollen legt jemand diese Datei an — sie muss also mit dem
        // serialisierten Modell uebereinstimmen.
        var geschrieben = new AccessPolicy { RequiredGroup = "G", ContactHint = "H" };
        File.WriteAllText(PolicyPath, JsonSerializer.Serialize(geschrieben));

        var gelesen = AccessPolicy.Load(PolicyPath);

        gelesen.RequiredGroup.Should().Be("G");
        gelesen.ContactHint.Should().Be("H");
    }
}

/// <summary>Auflösung der konfigurierten Gruppe zu einer SID.</summary>
[SupportedOSPlatform("windows")]
public class AccessGateTests
{
    [Fact]
    public void ResolveGroupSid_nimmt_eine_SID_direkt_an()
    {
        // S-1-5-32-544 = Vordefiniert\Administratoren, existiert auf jedem
        // Windows-System und braucht keine Domaene.
        var sid = AccessGate.ResolveGroupSid("S-1-5-32-544");

        sid.Should().NotBeNull();
        sid!.Value.Should().Be("S-1-5-32-544");
    }

    [Fact]
    public void ResolveGroupSid_loest_einen_bekannten_Namen_auf()
    {
        WindowsOnly.Require(OperatingSystem.IsWindows());

        // Ueber die well-known SID den lokalisierten Namen holen — auf einem
        // deutschen Windows heisst die Gruppe "Jeder", auf einem englischen
        // "Everyone". Der Test darf davon nicht abhaengen.
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        string name = everyone.Translate(typeof(NTAccount)).Value;

        AccessGate.ResolveGroupSid(name)!.Value.Should().Be(everyone.Value);
    }

    [Fact]
    public void ResolveGroupSid_meldet_einen_unbekannten_Namen_als_null()
    {
        // Ein Tippfehler im Gruppennamen darf nicht still zur Erlaubnis werden.
        AccessGate.ResolveGroupSid("Gruppe-die-es-nicht-gibt-" + Guid.NewGuid().ToString("N"))
            .Should().BeNull();
    }

    [Fact]
    public void CheckToken_laesst_ohne_Regel_durch()
    {
        var gate = new AccessGate(new AccessPolicy(), new NullDirectory());

        var result = gate.CheckToken();

        result.Granted.Should().BeTrue();
        result.Source.Should().Be(AccessCheckSource.NoPolicy);
    }

    [Fact]
    public void CheckToken_sperrt_bei_defekter_Regel()
    {
        var gate = new AccessGate(
            new AccessPolicy { RequiredGroup = " unreadable", ContactHint = "kaputt" },
            new NullDirectory());

        var result = gate.CheckToken();

        result.Granted.Should().BeFalse();
        result.Source.Should().Be(AccessCheckSource.Unresolved);
    }

    [Fact]
    public void CheckToken_sperrt_bei_unaufloesbarer_Gruppe()
    {
        var gate = new AccessGate(
            new AccessPolicy { RequiredGroup = "gibt-es-nicht-" + Guid.NewGuid().ToString("N") },
            new NullDirectory());

        var result = gate.CheckToken();

        result.Granted.Should().BeFalse();
        result.Source.Should().Be(AccessCheckSource.Unresolved);
        result.Problem.Should().Contain("nicht auffindbar");
    }

    [Fact]
    public void CheckToken_erkennt_eine_Gruppe_aus_dem_eigenen_Anmeldetoken()
    {
        WindowsOnly.Require(OperatingSystem.IsWindows());

        // "Jeder" steckt im Token jedes angemeldeten Kontos — damit laesst sich
        // der Erfolgsfall ohne Domaene pruefen.
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var gate = new AccessGate(
            new AccessPolicy { RequiredGroup = everyone.Value }, new NullDirectory());

        var result = gate.CheckToken();

        result.Granted.Should().BeTrue();
        result.Source.Should().Be(AccessCheckSource.LogonToken);
        result.Account.Should().Contain(Environment.UserName);
    }

    [Fact]
    public async Task CheckAsync_fragt_bei_einem_Token_Nein_im_Verzeichnis_nach()
    {
        WindowsOnly.Require(OperatingSystem.IsWindows());

        // Lokale Administratoren-SID: im Token eines normalen Kontos nicht
        // enthalten, also faellt die Pruefung auf das Verzeichnis zurueck.
        var directory = new NullDirectory { AntwortAufMitgliedschaft = true };
        var gate = new AccessGate(
            new AccessPolicy { RequiredGroup = "S-1-5-21-1-2-3-1234" }, directory);

        var result = await gate.CheckAsync(TestContext.Current.CancellationToken);

        directory.Gefragt.Should().BeTrue("das Token allein darf nicht das letzte Wort haben");
        result.Granted.Should().BeTrue();
        result.Source.Should().Be(AccessCheckSource.Directory);
    }

    [Fact]
    public async Task CheckAsync_meldet_ein_stummes_Verzeichnis_als_Verbindungsproblem()
    {
        WindowsOnly.Require(OperatingSystem.IsWindows());

        // Der Nutzer soll unterscheiden koennen: "du bist nicht in der Gruppe"
        // gegen "wir konnten es nicht pruefen".
        var directory = new NullDirectory { Fehler = new DirectoryAccessException("kein DC") };
        var gate = new AccessGate(
            new AccessPolicy { RequiredGroup = "S-1-5-21-1-2-3-1234" }, directory);

        var result = await gate.CheckAsync(TestContext.Current.CancellationToken);

        result.Granted.Should().BeFalse();
        result.Source.Should().Be(AccessCheckSource.Unresolved);
        result.Problem.Should().Contain("VPN");
    }

    [Fact]
    public async Task CheckAsync_spart_die_Abfrage_wenn_das_Token_schon_ja_sagt()
    {
        WindowsOnly.Require(OperatingSystem.IsWindows());

        var directory = new NullDirectory();
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var gate = new AccessGate(new AccessPolicy { RequiredGroup = everyone.Value }, directory);

        await gate.CheckAsync(TestContext.Current.CancellationToken);

        directory.Gefragt.Should().BeFalse("der schnelle Weg soll den Normalfall allein beantworten");
    }

    /// <summary>Verzeichnis-Attrappe: die Zugangsprüfung braucht keine Domäne.</summary>
    private sealed class NullDirectory : IDirectoryService
    {
        public bool Gefragt { get; private set; }
        public bool AntwortAufMitgliedschaft { get; init; }
        public Exception? Fehler { get; init; }

        public DirectoryConnectionInfo? ConnectionInfo => null;

        public Task<bool> IsMemberOfGroupAsync(string samAccountName, byte[] groupSid, CancellationToken ct)
        {
            Gefragt = true;
            if (Fehler is not null) return Task.FromException<bool>(Fehler);
            return Task.FromResult(AntwortAufMitgliedschaft);
        }

        public Task<DirectoryConnectionInfo> ConnectAsync(CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<Models.UserSearchHit>> SearchUsersAsync(string q, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<Models.AdUser?> LoadUserAsync(string dn, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<Models.AdGroup>> LoadGroupsAsync(
            Models.AdUser user, bool nested, CancellationToken ct) => throw new NotSupportedException();
        public void Invalidate() { }
    }
}
