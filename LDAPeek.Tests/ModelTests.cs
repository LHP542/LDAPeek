using FluentAssertions;
using LDAPeek.Models;
using LDAPeek.Services;

namespace LDAPeek.Tests;

/// <summary>Anzeige- und Exportlogik der Modelle.</summary>
public class AdGroupTests
{
    private static AdGroup Group(string name = "FB 5424 Lesen", MembershipKind membership = MembershipKind.Direct) =>
        new()
        {
            DistinguishedName = $"CN={name},OU=Gruppen,DC=lhp,DC=intern",
            Name = name,
            SamAccountName = name.Replace(' ', '-'),
            Description = "Zugriff auf die Ablage",
            Scope = GroupScope.Global,
            Kind = GroupKind.Security,
            Membership = membership,
        };

    [Fact]
    public void DisplayName_faellt_ueber_den_DN_zurueck()
    {
        var group = new AdGroup { DistinguishedName = "CN=Ohne Namen,DC=lhp" };

        group.DisplayName.Should().Be("Ohne Namen");
    }

    [Fact]
    public void MembershipOrder_stellt_die_Primaergruppe_nach_vorn()
    {
        // Oben steht, was dem Konto selbst zugewiesen wurde; darunter, was
        // daraus folgt.
        var order = new[] { MembershipKind.Nested, MembershipKind.Primary, MembershipKind.Direct }
            .Select(m => Group(membership: m))
            .OrderBy(g => g.MembershipOrder)
            .Select(g => g.Membership);

        order.Should().Equal(MembershipKind.Primary, MembershipKind.Direct, MembershipKind.Nested);
    }

    [Fact]
    public void Die_Herkunftsflags_passen_zur_Mitgliedschaft()
    {
        Group(membership: MembershipKind.Direct).IsDirect.Should().BeTrue();
        Group(membership: MembershipKind.Nested).IsNested.Should().BeTrue();
        Group(membership: MembershipKind.Primary).IsPrimary.Should().BeTrue();
        Group().IsSecurity.Should().BeTrue();
    }

    [Fact]
    public void ToCsvRow_schreibt_alle_Spalten_der_Kopfzeile()
    {
        int columns = AdGroup.CsvHeader.Split(';').Length;

        Group().ToCsvRow().Split(';').Length.Should().Be(columns);
    }

    [Fact]
    public void ToCsvRow_quotet_Werte_mit_Semikolon()
    {
        // Semikolon als Trenner, weil Excel im deutschen Gebietsschema danach
        // trennt — ein Semikolon in der Beschreibung würde die Spalten
        // verschieben.
        var group = new AdGroup
        {
            DistinguishedName = "CN=X,DC=lhp",
            Name = "X",
            Description = "Ablage; Drucker",
        };

        group.ToCsvRow().Should().Contain("\"Ablage; Drucker\"");
    }

    [Fact]
    public void ToCsvRow_verdoppelt_Anfuehrungszeichen()
    {
        var group = new AdGroup
        {
            DistinguishedName = "CN=X,DC=lhp",
            Name = "X",
            Description = "Der \"gute\" Ordner",
        };

        group.ToCsvRow().Should().Contain("\"Der \"\"gute\"\" Ordner\"");
    }

    [Fact]
    public void SubtitleText_faellt_auf_den_Pfad_zurueck()
    {
        var group = new AdGroup { DistinguishedName = "CN=X,OU=Gruppen,DC=lhp,DC=intern" };

        group.SubtitleText.Should().Be("lhp.intern / Gruppen");
    }
}

/// <summary>Berechnete Zustände eines Kontos.</summary>
public class AdUserTests
{
    private static AdUser User(AccountFlags flags = AccountFlags.NormalAccount) => new()
    {
        DistinguishedName = "CN=Hans Müller,OU=Benutzer,DC=lhp,DC=intern",
        DisplayName = "Müller, Hans",
        SamAccountName = "mueller",
        Flags = flags,
    };

    [Fact]
    public void StatusText_meldet_ein_aktives_Konto()
    {
        var user = User();

        user.StatusText.Should().Be("Aktiv");
        user.IsHealthy.Should().BeTrue();
    }

    [Fact]
    public void StatusText_meldet_ein_deaktiviertes_Konto()
    {
        var user = User(AccountFlags.NormalAccount | AccountFlags.Disabled);

        user.StatusText.Should().Be("Deaktiviert");
        user.IsHealthy.Should().BeFalse();
    }

    [Fact]
    public void Ein_gesetzter_Sperrzeitpunkt_bedeutet_gesperrt()
    {
        // AD meldet nicht "gesperrt: ja/nein", sondern nur den Zeitpunkt.
        var user = new AdUser
        {
            DistinguishedName = "CN=X,DC=lhp",
            LockedSince = DateTimeOffset.Now.AddMinutes(-5),
        };

        user.IsLockedOut.Should().BeTrue();
        user.StatusText.Should().Be("Gesperrt");
    }

    [Fact]
    public void Ein_abgelaufenes_Konto_wird_erkannt()
    {
        var user = new AdUser
        {
            DistinguishedName = "CN=X,DC=lhp",
            AccountExpires = DateTimeOffset.Now.AddDays(-1),
        };

        user.IsExpired.Should().BeTrue();
        user.StatusText.Should().Be("Abgelaufen");
    }

    [Fact]
    public void AccountExpiresText_sagt_nie_statt_eines_Datums_von_1601() =>
        User().AccountExpiresText.Should().Be("läuft nie ab");

    [Fact]
    public void PasswordExpiresText_beruecksichtigt_das_Flag()
    {
        User(AccountFlags.NormalAccount | AccountFlags.DontExpirePassword)
            .PasswordExpiresText.Should().Be("läuft nie ab");
    }

    [Fact]
    public void PasswordLastSetText_zeigt_den_Aenderungszwang()
    {
        var user = new AdUser { DistinguishedName = "CN=X,DC=lhp", MustChangePassword = true };

        user.PasswordLastSetText.Should().Contain("nächster Anmeldung");
    }

    [Fact]
    public void PasswordExpiresSoon_greift_nur_im_Vierzehn_Tage_Fenster()
    {
        AdUser WithExpiry(DateTimeOffset? when) =>
            new() { DistinguishedName = "CN=X,DC=lhp", PasswordExpires = when };

        WithExpiry(DateTimeOffset.Now.AddDays(3)).PasswordExpiresSoon.Should().BeTrue();
        WithExpiry(DateTimeOffset.Now.AddDays(30)).PasswordExpiresSoon.Should().BeFalse();
        WithExpiry(DateTimeOffset.Now.AddDays(-1)).PasswordExpiresSoon.Should().BeFalse();
        WithExpiry(null).PasswordExpiresSoon.Should().BeFalse();
    }

    [Fact]
    public void Leere_Felder_zeigen_ueberall_dasselbe_Zeichen()
    {
        var user = User();

        user.MailText.Should().Be(Display.Empty);
        user.DepartmentText.Should().Be(Display.Empty);
        user.OfficeText.Should().Be(Display.Empty);
        user.AddressText.Should().Be(Display.Empty);
    }

    [Fact]
    public void AddressText_setzt_Strasse_PLZ_und_Ort_zusammen()
    {
        var user = new AdUser
        {
            DistinguishedName = "CN=X,DC=lhp",
            Street = "Friedrich-Ebert-Str. 79/81",
            PostalCode = "14469",
            City = "Potsdam",
        };

        user.AddressText.Should().Be("Friedrich-Ebert-Str. 79/81, 14469 Potsdam");
    }

    [Fact]
    public void AddressText_kommt_mit_Teilangaben_zurecht()
    {
        var user = new AdUser { DistinguishedName = "CN=X,DC=lhp", City = "Potsdam" };

        user.AddressText.Should().Be("Potsdam");
    }

    [Fact]
    public void FlagsText_sagt_etwas_auch_wenn_nichts_gesetzt_ist() =>
        User().FlagsText.Should().Be("keine Besonderheiten");

    [Fact]
    public void BestName_faellt_ueber_cn_und_Anmeldenamen_zurueck()
    {
        new AdUser { DistinguishedName = "CN=X,DC=lhp", CommonName = "Hans" }.BestName.Should().Be("Hans");
        new AdUser { DistinguishedName = "CN=X,DC=lhp", SamAccountName = "hm" }.BestName.Should().Be("hm");
        new AdUser { DistinguishedName = "CN=X,DC=lhp" }.BestName.Should().Be("CN=X,DC=lhp");
    }
}

/// <summary>Trefferliste.</summary>
public class UserSearchHitTests
{
    [Fact]
    public void SubtitleText_nennt_Anmeldenamen_und_Abteilung()
    {
        var hit = new UserSearchHit
        {
            DistinguishedName = "CN=X,DC=lhp",
            SamAccountName = "mueller",
            Department = "FB 5424",
        };

        hit.SubtitleText.Should().Be("mueller · FB 5424");
    }

    [Fact]
    public void SubtitleText_kommt_ohne_Abteilung_aus()
    {
        var hit = new UserSearchHit { DistinguishedName = "CN=X,DC=lhp", SamAccountName = "mueller" };

        hit.SubtitleText.Should().Be("mueller");
    }

    [Fact]
    public void Ein_Konto_ohne_Anmeldenamen_zeigt_einen_Platzhalter()
    {
        new UserSearchHit { DistinguishedName = "CN=X,DC=lhp" }.SubtitleText.Should().Be("—");
    }
}

/// <summary>Formatierung.</summary>
public class DisplayTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Text_ersetzt_Leerwerte(string? value) =>
        Display.Text(value).Should().Be(Display.Empty);

    [Fact]
    public void Date_liefert_bei_null_dasselbe_Zeichen_wie_Text() =>
        Display.Date(null).Should().Be(Display.Empty);

    [Fact]
    public void DateWithAge_nennt_den_Abstand_zu_heute()
    {
        // Bei "letzte Anmeldung" ist die eigentliche Frage, ob das Konto noch
        // benutzt wird — der absolute Wert allein beantwortet sie nicht.
        Display.DateWithAge(DateTimeOffset.Now.AddDays(-5)).Should().Contain("vor 5 Tagen");
        Display.DateWithAge(DateTimeOffset.Now.AddDays(-400)).Should().Contain("Monaten");
        Display.DateWithAge(DateTimeOffset.Now.AddDays(-800)).Should().Contain("Jahren");
        Display.DateWithAge(DateTimeOffset.Now.AddMinutes(-30)).Should().Contain("Minuten");
    }

    [Fact]
    public void DateWithAge_enthaelt_immer_auch_das_absolute_Datum()
    {
        var moment = DateTimeOffset.Now.AddDays(-3);

        Display.DateWithAge(moment).Should().StartWith(moment.ToString(Display.DateTimeFormat));
    }

    [Fact]
    public void DateWithAge_kommt_mit_der_Zukunft_zurecht()
    {
        Display.DateWithAge(DateTimeOffset.Now.AddDays(10)).Should().Contain("in 10 Tagen");
    }
}

/// <summary>Textbericht für die Zwischenablage.</summary>
public class UserReportTests
{
    [Fact]
    public void Build_nennt_Stammdaten_und_Gruppen()
    {
        var user = new AdUser
        {
            DistinguishedName = "CN=Hans Müller,OU=Benutzer,DC=lhp,DC=intern",
            DisplayName = "Müller, Hans",
            SamAccountName = "mueller",
            Mail = "hans.mueller@lhp.intern",
            Department = "FB 5424",
        };
        var groups = new[]
        {
            new AdGroup
            {
                DistinguishedName = "CN=Domänen-Benutzer,CN=Users,DC=lhp,DC=intern",
                Name = "Domänen-Benutzer",
                Kind = GroupKind.Security,
                Scope = GroupScope.Global,
                Membership = MembershipKind.Primary,
            },
        };

        string report = UserReport.Build(user, groups);

        report.Should().Contain("Müller, Hans");
        report.Should().Contain("mueller");
        report.Should().Contain("hans.mueller@lhp.intern");
        report.Should().Contain("FB 5424");
        report.Should().Contain("Gruppen (1)");
        report.Should().Contain("Domänen-Benutzer");
        report.Should().Contain("Primärgruppe");
        report.Should().Contain("LDAPeek");
    }

    [Fact]
    public void Build_laesst_leere_Felder_weg()
    {
        // Ein Bericht voller "—" ist schlechter lesbar als einer, der nur das
        // Vorhandene nennt.
        var user = new AdUser { DistinguishedName = "CN=X,DC=lhp", SamAccountName = "x" };

        string report = UserReport.Build(user, []);

        report.Should().NotContain("E-Mail");
        report.Should().NotContain("Abteilung");
        report.Should().Contain("Anmeldename");
    }

    [Fact]
    public void Build_meldet_wenn_keine_Gruppen_da_sind()
    {
        var user = new AdUser { DistinguishedName = "CN=X,DC=lhp" };

        UserReport.Build(user, []).Should().Contain("(keine)");
    }
}
