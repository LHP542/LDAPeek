using FluentAssertions;
using LDAPeek.Models;
using LDAPeek.Services;

namespace LDAPeek.Tests;

/// <summary>
/// Absicherung der Gliederung. Der Baum ist die Antwort auf „woher kommt das
/// Recht?" — und genau dort darf nichts verschwinden, doppelt auftauchen oder
/// den Aufbau zum Hängen bringen.
/// </summary>
public class GroupTreeTests
{
    private const string Base = "OU=Gruppen,DC=example,DC=intern";

    private static string Dn(string name) => $"CN={name},{Base}";

    /// <summary>
    /// <paramref name="bringt"/> sind die Gruppen, die eine Mitgliedschaft in
    /// dieser Gruppe <b>zusätzlich einbringt</b> — im Verzeichnis ist das
    /// <c>memberOf</c> der Gruppe, in der Anzeige stehen sie darunter.
    ///
    /// Die Benennung ist Absicht: „memberOf" lädt zum Verdrehen der Richtung
    /// ein, und ein Test, der dieselbe Verwechslung macht wie der Code, den er
    /// prüfen soll, ist grün und wertlos. Genau das ist beim ersten Anlauf
    /// passiert und erst gegen die echte Domäne aufgefallen.
    /// </summary>
    private static AdGroup Group(
        string name, MembershipKind membership = MembershipKind.Nested,
        string[]? bringt = null, GroupKind kind = GroupKind.Security,
        string? path = null, string[]? bringtFremde = null) => new()
        {
            DistinguishedName = path is null ? Dn(name) : $"CN={name},{path}",
            Name = name,
            Kind = kind,
            Scope = GroupScope.Global,
            Membership = membership,
            MemberOfDns = [.. (bringt ?? []).Select(Dn), .. bringtFremde ?? []],
        };

    private static bool All(AdGroup group) => true;

    private static IReadOnlyList<GroupNode> Nesting(
        IReadOnlyList<AdGroup> groups, Func<AdGroup, bool>? matches = null) =>
        GroupTree.Build(groups, GroupViewMode.Nesting, matches ?? All);

    // ------------------------------------------------------------------
    // Aufbau
    // ------------------------------------------------------------------

    [Fact]
    public void Direkte_Gruppen_sind_die_Wurzeln()
    {
        AdGroup[] groups =
        [
            Group("Primaer", MembershipKind.Primary),
            Group("Direkt", MembershipKind.Direct, bringt: ["Geerbt"]),
            Group("Geerbt"),
        ];

        var roots = Nesting(groups);

        roots.Select(n => n.Title).Should().Equal("Primaer", "Direkt");
        roots[1].Children.Single().Title.Should().Be("Geerbt");
    }

    /// <summary>
    /// Der Fall, an dem die Richtung hängt: Die Rollengruppe steht oben, die
    /// Ressourcengruppen, die sie mitbringt, darunter — nicht umgekehrt.
    /// </summary>
    [Fact]
    public void Unter_einer_Gruppe_steht_was_sie_einbringt()
    {
        AdGroup[] groups =
        [
            Group("AP-FB42", MembershipKind.Direct, bringt: ["RG-Ablage-Schreiben"]),
            Group("RG-Ablage-Schreiben"),
        ];

        var root = Nesting(groups).Single();

        root.Title.Should().Be("AP-FB42");
        root.Children.Single().Title.Should().Be("RG-Ablage-Schreiben");
    }

    [Fact]
    public void Verschachtelung_wird_ueber_mehrere_Ebenen_aufgespannt()
    {
        AdGroup[] groups =
        [
            Group("Profil", MembershipKind.Direct, bringt: ["Fachverfahren"]),
            Group("Fachverfahren", bringt: ["Redaktion"]),
            Group("Redaktion"),
        ];

        var root = Nesting(groups).Single();

        root.Children.Single().Title.Should().Be("Fachverfahren");
        root.Children.Single().Children.Single().Title.Should().Be("Redaktion");
    }

    /// <summary>
    /// Der Grund, warum überhaupt eine Entscheidung nötig ist: Mitgliedschaften
    /// bilden ein Geflecht, keinen Baum. Jeder Weg einzeln aufgeklappt bläht die
    /// Liste auf — deshalb einmal einhängen, den Rest daneben schreiben.
    /// </summary>
    [Fact]
    public void Eine_Gruppe_erscheint_nur_einmal_und_nennt_die_anderen_Wege()
    {
        AdGroup[] groups =
        [
            Group("Profil", MembershipKind.Direct, bringt: ["Fachverfahren", "Redaktion"]),
            Group("Fachverfahren", bringt: ["Redaktion"]),
            Group("Redaktion"),
        ];

        var root = Nesting(groups).Single();

        // Kürzester Weg gewinnt: direkt unter dem Profil, nicht über Fachverfahren.
        root.Children.Select(n => n.Title).Should().Equal("Fachverfahren", "Redaktion");
        root.AllGroups().Should().HaveCount(3);

        var redaktion = root.Children.Single(n => n.Title == "Redaktion");
        redaktion.AlsoVia.Should().Equal("Fachverfahren");
        redaktion.AlsoViaText.Should().Be("auch über Fachverfahren");
    }

    [Fact]
    public void Jede_Gruppe_bleibt_genau_einmal_erhalten()
    {
        AdGroup[] groups =
        [
            Group("Profil", MembershipKind.Direct, bringt: ["A", "B"]),
            Group("A", bringt: ["B", "C"]),
            Group("B", bringt: ["C"]),
            Group("C"),
        ];

        var alle = Nesting(groups).SelectMany(n => n.AllGroups()).ToList();

        alle.Should().HaveCount(4);
        alle.Select(g => g.DisplayName).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Ein Ring in den Mitgliedschaften darf den Aufbau nicht zum Hängen
    /// bringen. AD verhindert Ringe innerhalb einer Domäne, über Domänengrenzen
    /// hinweg ist die Zusicherung aber schwächer — und ein hängendes Fenster
    /// wäre die schlechteste denkbare Antwort.
    /// </summary>
    [Fact]
    public void Ein_Ring_fuehrt_nicht_zu_einer_Endlosschleife()
    {
        AdGroup[] groups =
        [
            Group("Direkt", MembershipKind.Direct, bringt: ["A"]),
            Group("A", bringt: ["B"]),
            Group("B", bringt: ["C"]),
            Group("C", bringt: ["A"]),
        ];

        var roots = Nesting(groups);

        roots.SelectMany(n => n.AllGroups()).Should().HaveCount(4);
    }

    /// <summary>
    /// Ein Ring ganz ohne Verbindung zu einer direkten Mitgliedschaft: Die
    /// Gruppen sind unerreichbar, dürfen aber nicht verschwinden.
    /// </summary>
    [Fact]
    public void Ein_abgekoppelter_Ring_geht_nicht_verloren()
    {
        AdGroup[] groups =
        [
            Group("Direkt", MembershipKind.Direct),
            Group("X", bringt: ["Y"]),
            Group("Y", bringt: ["X"]),
        ];

        var roots = Nesting(groups);

        roots.Select(n => n.Title).Should().BeEquivalentTo("Direkt", "X", "Y");
    }

    /// <summary>
    /// Eine geerbte Gruppe, deren Zwischenstation nicht im Ergebnis steht (etwa
    /// eine Gruppe aus einer fremden Domäne), bleibt sichtbar — als eigene
    /// Wurzel. Sie stillschweigend wegzulassen wäre die schlimmste Variante:
    /// Das Werkzeug soll ja gerade verlässlich Auskunft geben.
    /// </summary>
    [Fact]
    public void Eine_Gruppe_ohne_bekannten_Weg_bleibt_sichtbar()
    {
        AdGroup[] groups =
        [
            Group("Direkt", MembershipKind.Direct),
            Group("Fremd", bringtFremde: ["CN=Unbekannt,DC=andere,DC=domaene"]),
        ];

        Nesting(groups).Select(n => n.Title).Should().BeEquivalentTo("Direkt", "Fremd");
    }

    [Fact]
    public void Eine_direkte_Gruppe_bleibt_Wurzel_auch_wenn_sie_verschachtelt_waere()
    {
        AdGroup[] groups =
        [
            Group("Oben", MembershipKind.Direct, bringt: ["Unten"]),
            Group("Unten", MembershipKind.Direct),
        ];

        var roots = Nesting(groups);

        roots.Select(n => n.Title).Should().Equal("Oben", "Unten");
        roots[0].Children.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // Filtern
    // ------------------------------------------------------------------

    [Fact]
    public void Der_Weg_zu_einem_Treffer_bleibt_als_Zusammenhang_stehen()
    {
        AdGroup[] groups =
        [
            Group("Profil", MembershipKind.Direct, bringt: ["Zwischenstufe"]),
            Group("Zwischenstufe", bringt: ["Gesucht"]),
            Group("Gesucht"),
        ];

        var root = Nesting(groups, g => g.DisplayName == "Gesucht").Single();

        root.Title.Should().Be("Profil");
        root.IsContext.Should().BeTrue("die Wurzel ist nur der Weg zum Treffer");

        var treffer = root.Children.Single().Children.Single();
        treffer.Title.Should().Be("Gesucht");
        treffer.IsContext.Should().BeFalse();
    }

    [Fact]
    public void Ein_Ast_ohne_Treffer_faellt_weg()
    {
        AdGroup[] groups =
        [
            Group("Profil", MembershipKind.Direct, bringt: ["Gesucht"]),
            Group("Gesucht"),
            Group("Anderes", MembershipKind.Direct),
        ];

        Nesting(groups, g => g.DisplayName == "Gesucht")
            .Select(n => n.Title).Should().Equal("Profil");
    }

    // ------------------------------------------------------------------
    // Verzeichnis- und flache Ansicht
    // ------------------------------------------------------------------

    [Fact]
    public void Verzeichnisansicht_gruppiert_nach_Organisationspfad()
    {
        AdGroup[] groups =
        [
            Group("A", path: "OU=Fachbereich,DC=example,DC=intern"),
            Group("B", path: "OU=Fachbereich,DC=example,DC=intern"),
            Group("C", path: "OU=Dienste,DC=example,DC=intern"),
        ];

        var headers = GroupTree.Build(groups, GroupViewMode.Organization, All);

        headers.Should().HaveCount(2);
        headers.Should().OnlyContain(n => n.IsHeader);
        headers.Select(n => n.Header).Should().Equal(
            "example.intern / Dienste", "example.intern / Fachbereich");
        headers[1].Children.Select(n => n.Title).Should().Equal("A", "B");
        headers[1].CountText.Should().Be("2");
    }

    [Fact]
    public void Verzeichnisansicht_faengt_Gruppen_ohne_Pfad_auf()
    {
        AdGroup[] groups = [new() { DistinguishedName = "CN=Ohne", Name = "Ohne" }];

        GroupTree.Build(groups, GroupViewMode.Organization, All)
            .Single().Header.Should().Be(GroupTree.NoPathHeader);
    }

    [Fact]
    public void Flache_Ansicht_zeigt_alle_Gruppen_ohne_Gliederung()
    {
        AdGroup[] groups =
        [
            Group("Profil", MembershipKind.Direct, bringt: ["Geerbt"]),
            Group("Geerbt"),
        ];

        var nodes = GroupTree.Build(groups, GroupViewMode.Flat, All);

        nodes.Should().HaveCount(2);
        nodes.Should().OnlyContain(n => n.Children.Count == 0);
    }

    /// <summary>
    /// In der flachen Ansicht ist der Pfad die einzige Einordnung, die eine
    /// Gruppe ohne Beschreibung noch hat — dort bleibt er als zweite Zeile.
    /// Im Baum und in der Verzeichnisansicht stünde er in jeder Zeile.
    /// </summary>
    [Fact]
    public void Nur_die_flache_Ansicht_nutzt_den_Pfad_als_zweite_Zeile()
    {
        AdGroup[] groups = [Group("Ohne Beschreibung", MembershipKind.Direct)];

        GroupTree.Build(groups, GroupViewMode.Flat, All)
            .Single().Subtitle.Should().Be("example.intern / Gruppen");

        GroupTree.Build(groups, GroupViewMode.Nesting, All)
            .Single().Subtitle.Should().BeNull();
    }

    [Fact]
    public void Filter_wirkt_in_allen_Ansichten()
    {
        AdGroup[] groups =
        [
            Group("Behalten", MembershipKind.Direct),
            Group("Weg", MembershipKind.Direct),
        ];

        foreach (var mode in Enum.GetValues<GroupViewMode>())
        {
            GroupTree.Build(groups, mode, g => g.DisplayName == "Behalten")
                .SelectMany(n => n.AllGroups())
                .Select(g => g.DisplayName)
                .Should().Equal(["Behalten"], $"die Ansicht {mode} muss denselben Filter anwenden");
        }
    }

    [Fact]
    public void Eine_leere_Liste_ergibt_eine_leere_Ansicht()
    {
        foreach (var mode in Enum.GetValues<GroupViewMode>())
        {
            GroupTree.Build([], mode, All).Should().BeEmpty();
        }
    }
}
