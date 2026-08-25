using System.Runtime.Versioning;
using FluentAssertions;
using LDAPeek.Models;
using LDAPeek.Services;
using LDAPeek.ViewModels;

namespace LDAPeek.Tests;

/// <summary>
/// Die Teile des Verzeichnisdienstes, die ohne laufende Domäne prüfbar sind.
/// Der eigentliche LDAP-Verkehr wird bewusst nicht nachgebaut — ein Mock des
/// Protokolls würde vor allem den Mock testen.
/// </summary>
[SupportedOSPlatform("windows")]
public class DirectoryServiceTests
{
    [Fact]
    public void ParseCredential_versteht_die_NetBIOS_Schreibweise()
    {
        var credential = DirectoryService.ParseCredential(@"LHP\a-osteL", "geheim");

        credential.Domain.Should().Be("LHP");
        credential.UserName.Should().Be("a-osteL");
        credential.Password.Should().Be("geheim");
    }

    [Fact]
    public void ParseCredential_versteht_den_Benutzerprinzipalnamen()
    {
        var credential = DirectoryService.ParseCredential("a-osteL@lhp.intern", "geheim");

        credential.Domain.Should().Be("lhp.intern");
        credential.UserName.Should().Be("a-osteL");
    }

    [Fact]
    public void ParseCredential_ohne_Domaenenteil_ueberlaesst_Windows_die_Wahl()
    {
        var credential = DirectoryService.ParseCredential("a-osteL", "geheim");

        credential.Domain.Should().BeEmpty();
        credential.UserName.Should().Be("a-osteL");
    }

    [Fact]
    public void ParseCredential_verkraftet_ein_leeres_Passwort()
    {
        DirectoryService.ParseCredential(@"LHP\x", null).Password.Should().BeNullOrEmpty();
    }

    [Fact]
    public void DefaultDomain_nimmt_den_DNS_Namen_der_Anmeldedomaene()
    {
        // Der DC-Locator braucht den DNS-Namen (lhp.intern), nicht den
        // NetBIOS-Namen — deshalb hat USERDNSDOMAIN Vorrang.
        //
        // Die Erwartung muss dieselbe Leer-Semantik benutzen wie die Produktion
        // (IsNullOrWhiteSpace), nicht bloß "?? ". Sonst gehen die beiden bei
        // einer auf Leerstring GESETZTEN Variable auseinander: der Test erwartet
        // dann "", während DefaultDomain() korrekt auf USERDOMAIN ausweicht.
        // Auf einem nicht domänengebundenen Rechner — etwa einem CI-Runner —
        // ist genau das der Normalfall.
        static string? NonEmpty(string name) =>
            Environment.GetEnvironmentVariable(name) is { } v && !string.IsNullOrWhiteSpace(v) ? v : null;

        string? expected = NonEmpty("USERDNSDOMAIN") ?? NonEmpty("USERDOMAIN");

        if (expected is null)
        {
            // Weder DNS- noch NetBIOS-Domäne gesetzt: dann MUSS DefaultDomain()
            // sauber scheitern statt einen leeren Servernamen zurückzugeben.
            Action act = () => DirectoryService.DefaultDomain();
            act.Should().Throw<DirectoryAccessException>();
            return;
        }

        DirectoryService.DefaultDomain().Should().Be(expected);
    }
}

/// <summary>Filterlogik der Gruppenliste.</summary>
[SupportedOSPlatform("windows")]
public class GroupFilterTests
{
    private static AdGroup Group(string name, string? sam = null, string? description = null) => new()
    {
        DistinguishedName = $"CN={name},OU=Gruppen,DC=lhp,DC=intern",
        Name = name,
        SamAccountName = sam,
        Description = description,
    };

    [Fact]
    public void MatchesFilter_sucht_im_Namen()
    {
        MainWindowViewModel.MatchesFilter(Group("FB 5424 Lesen"), "5424").Should().BeTrue();
    }

    [Fact]
    public void MatchesFilter_ignoriert_die_Gross_und_Kleinschreibung()
    {
        MainWindowViewModel.MatchesFilter(Group("FB 5424 Lesen"), "lesen").Should().BeTrue();
    }

    [Fact]
    public void MatchesFilter_sucht_auch_im_Anmeldenamen_und_in_der_Beschreibung()
    {
        MainWindowViewModel.MatchesFilter(Group("X", sam: "grp-ablage"), "ablage").Should().BeTrue();
        MainWindowViewModel.MatchesFilter(Group("X", description: "Zugriff Drucker"), "drucker").Should().BeTrue();
    }

    [Fact]
    public void MatchesFilter_sucht_NICHT_im_DN()
    {
        // Sonst würde eine Suche nach einer OU alle Gruppen darunter zu
        // Treffern machen — der Filter wäre wertlos.
        MainWindowViewModel.MatchesFilter(Group("FB 5424 Lesen"), "OU=Gruppen").Should().BeFalse();
    }

    [Fact]
    public void MatchesFilter_meldet_keinen_Treffer_bei_fremdem_Text()
    {
        MainWindowViewModel.MatchesFilter(Group("FB 5424 Lesen"), "buchhaltung").Should().BeFalse();
    }
}
