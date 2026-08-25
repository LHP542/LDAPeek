using FluentAssertions;
using LDAPeek.Models;
using LDAPeek.Services;

namespace LDAPeek.Tests;

/// <summary>
/// Umwandlung der AD-Rohwerte. Die Sonderwerte für „nie" sind hier das
/// eigentliche Thema: wer sie verwechselt, zeigt dem Nutzer
/// „Konto läuft ab am 01.01.1601".
/// </summary>
public class AdValueTests
{
    [Fact]
    public void FromFileTime_wandelt_einen_echten_Zeitstempel()
    {
        var expected = new DateTimeOffset(2026, 3, 14, 8, 12, 0, TimeSpan.Zero);

        AdValue.FromFileTime(expected.ToFileTime())
            .Should().BeCloseTo(expected.ToLocalTime(), TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData(0L)]                    // Attribut nie gesetzt
    [InlineData(long.MaxValue)]         // "läuft nie ab"
    [InlineData(-1L)]                   // kaputter Wert aus einer Migration
    public void FromFileTime_liefert_null_fuer_die_Sonderwerte(long value) =>
        AdValue.FromFileTime(value).Should().BeNull();

    [Fact]
    public void FromFileTime_verkraftet_Werte_ausserhalb_des_DateTime_Bereichs()
    {
        // Solche Werte kommen in gewachsenen Domänen tatsächlich vor.
        AdValue.FromFileTime(long.MaxValue - 1).Should().BeNull();
    }

    [Theory]
    [InlineData("20260314081200.0Z")]
    [InlineData("20260314081200Z")]
    [InlineData("20260314081200.123Z")]
    public void FromGeneralizedTime_versteht_die_gebraeuchlichen_Schreibweisen(string value)
    {
        var expected = new DateTimeOffset(2026, 3, 14, 8, 12, 0, TimeSpan.Zero);

        AdValue.FromGeneralizedTime(value)
            .Should().BeCloseTo(expected.ToLocalTime(), TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("kein Datum")]
    public void FromGeneralizedTime_liefert_null_bei_Unsinn(string? value) =>
        AdValue.FromGeneralizedTime(value).Should().BeNull();

    [Fact]
    public void MustChangePasswordAtNextLogon_erkennt_die_Null()
    {
        // pwdLastSet == 0 heißt NICHT "nie gesetzt", sondern "muss bei der
        // nächsten Anmeldung geändert werden" — für den Helpdesk der
        // eigentlich interessante Unterschied.
        AdValue.MustChangePasswordAtNextLogon(0).Should().BeTrue();
        AdValue.MustChangePasswordAtNextLogon(133_000_000_000_000_000).Should().BeFalse();
    }

    [Fact]
    public void LockedSince_liefert_null_fuer_ein_nicht_gesperrtes_Konto() =>
        AdValue.LockedSince(0).Should().BeNull();

    // ------------------------------------------------------------------
    // userAccountControl
    // ------------------------------------------------------------------

    [Fact]
    public void ParseAccountFlags_erkennt_ein_normales_aktives_Konto()
    {
        var flags = AdValue.ParseAccountFlags(512);   // NORMAL_ACCOUNT

        flags.Should().HaveFlag(AccountFlags.NormalAccount);
        flags.Should().NotHaveFlag(AccountFlags.Disabled);
    }

    [Fact]
    public void ParseAccountFlags_erkennt_ein_deaktiviertes_Konto()
    {
        AdValue.ParseAccountFlags(514)               // NORMAL_ACCOUNT | ACCOUNTDISABLE
            .Should().HaveFlag(AccountFlags.Disabled);
    }

    [Fact]
    public void ParseAccountFlags_erkennt_ein_nie_ablaufendes_Passwort()
    {
        AdValue.ParseAccountFlags(66048)             // NORMAL_ACCOUNT | DONT_EXPIRE_PASSWORD
            .Should().HaveFlag(AccountFlags.DontExpirePassword);
    }

    [Fact]
    public void DescribeAccountFlags_liefert_deutsche_Klartexte()
    {
        var texts = AdValue.DescribeAccountFlags(
            AccountFlags.Disabled | AccountFlags.DontExpirePassword);

        texts.Should().Contain("Deaktiviert");
        texts.Should().Contain("Passwort läuft nie ab");
    }

    [Fact]
    public void DescribeAccountFlags_ist_leer_bei_einem_unauffaelligen_Konto() =>
        AdValue.DescribeAccountFlags(AccountFlags.NormalAccount).Should().BeEmpty();

    // ------------------------------------------------------------------
    // groupType
    // ------------------------------------------------------------------

    [Fact]
    public void ParseGroupKind_erkennt_eine_Sicherheitsgruppe()
    {
        // -2147483646 = 0x80000002: SECURITY_ENABLED | GLOBAL. Das Bit setzt
        // das Vorzeichen — ein Vergleich auf einem int ohne unchecked-Cast
        // wäre still falsch.
        AdValue.ParseGroupKind(-2147483646).Should().Be(GroupKind.Security);
    }

    [Fact]
    public void ParseGroupKind_erkennt_eine_Verteilergruppe() =>
        AdValue.ParseGroupKind(2).Should().Be(GroupKind.Distribution);

    [Theory]
    [InlineData(-2147483646, GroupScope.Global)]        // 0x80000002
    [InlineData(-2147483644, GroupScope.DomainLocal)]   // 0x80000004
    [InlineData(-2147483640, GroupScope.Universal)]     // 0x80000008
    [InlineData(-2147483643, GroupScope.BuiltinLocal)]  // 0x80000005 (SYSTEM|DOMAIN_LOCAL)
    public void ParseGroupScope_liest_den_Gueltigkeitsbereich(int groupType, GroupScope expected) =>
        AdValue.ParseGroupScope(groupType).Should().Be(expected);

    [Fact]
    public void ParseGroupScope_erkennt_Builtin_vor_DomainLocal()
    {
        // Vordefinierte Gruppen tragen ZUSÄTZLICH das DomainLocal-Bit. Ohne
        // die richtige Prüfreihenfolge stünde bei "Vordefiniert\Administratoren"
        // "Lokal in Domäne".
        AdValue.ParseGroupScope(-2147483643).Should().Be(GroupScope.BuiltinLocal);
    }

    [Fact]
    public void ParseGroupScope_meldet_Unbekannt_statt_zu_raten() =>
        AdValue.ParseGroupScope(0).Should().Be(GroupScope.Unknown);

    [Fact]
    public void DescribeScope_und_DescribeKind_sind_deutsch()
    {
        AdValue.DescribeScope(GroupScope.DomainLocal).Should().Be("Lokal in Domäne");
        AdValue.DescribeScope(GroupScope.Universal).Should().Be("Universell");
        AdValue.DescribeKind(GroupKind.Security).Should().Be("Sicherheit");
        AdValue.DescribeMembership(MembershipKind.Nested).Should().Be("verschachtelt");
    }

    // ------------------------------------------------------------------
    // DN-Zerlegung
    // ------------------------------------------------------------------

    [Fact]
    public void SplitDn_trennt_nur_an_nicht_escapten_Kommas()
    {
        // In vielen Domänen ist "Nachname, Vorname" die Namenskonvention —
        // string.Split(',') zerlegt den Namen dann mitten durch.
        var parts = AdValue.SplitDn(@"CN=Müller\, Hans,OU=Benutzer,DC=lhp,DC=intern");

        parts.Should().HaveCount(4);
        parts[0].Should().Be(@"CN=Müller\, Hans");
        parts[1].Should().Be("OU=Benutzer");
    }

    [Fact]
    public void FriendlyNameFromDn_loest_das_escapte_Komma_auf()
    {
        AdValue.FriendlyNameFromDn(@"CN=Müller\, Hans,OU=Benutzer,DC=lhp,DC=intern")
            .Should().Be("Müller, Hans");
    }

    [Fact]
    public void FriendlyNameFromDn_liefert_den_einfachen_Fall() =>
        AdValue.FriendlyNameFromDn("CN=Hans Müller,DC=lhp,DC=intern").Should().Be("Hans Müller");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void FriendlyNameFromDn_verkraftet_leere_Eingaben(string? dn) =>
        AdValue.FriendlyNameFromDn(dn).Should().BeNull();

    [Fact]
    public void OrganizationalPath_liest_sich_von_aussen_nach_innen()
    {
        // Der DN selbst liest sich umgekehrt und ist für einen schnellen Blick
        // unbrauchbar.
        AdValue.OrganizationalPath("CN=Hans,OU=FB 5424,OU=Benutzer,DC=lhp,DC=intern")
            .Should().Be("lhp.intern / Benutzer / FB 5424");
    }

    [Fact]
    public void OrganizationalPath_kommt_ohne_Container_aus() =>
        AdValue.OrganizationalPath("CN=Hans,DC=lhp,DC=intern").Should().Be("lhp.intern");

    [Fact]
    public void OrganizationalPath_verkraftet_leere_Eingaben() =>
        AdValue.OrganizationalPath(null).Should().BeNull();
}
