using System.DirectoryServices.Protocols;
using System.Runtime.Versioning;
using System.Text;
using FluentAssertions;
using LDAPeek.Services;

namespace LDAPeek.Tests;

/// <summary>
/// Absicherung gegen die stillste aller AD-Fallen: mehrwertige Attribute, die
/// als leere Liste ankommen, obwohl der Domain Controller sie geliefert hat.
///
/// Real passiert (2026-09-03): <c>memberOf</c> kam mit 34 Werten vom DC, aber
/// LDAPeek zeigte „0 direkt, 133 verschachtelt" — jede Gruppe galt als geerbt,
/// weil die Liste der direkten Mitgliedschaften leer blieb. Ursache war eine
/// <c>foreach</c>-Schleife über das <see cref="DirectoryAttribute"/>, die die
/// Werte mit <c>value is string</c> prüft. Ein <c>foreach</c> liefert dort aber
/// die Rohwerte, und die sind <c>byte[]</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EntryReaderTests
{
    private const string GroupA = "CN=Gruppe A,OU=Gruppen,DC=example,DC=intern";
    private const string GroupB = "CN=Gruppe B,OU=Gruppen,DC=example,DC=intern";

    /// <summary>
    /// Der Kern des Regressionstests: So — als UTF-8-Bytes — kommen die Werte
    /// über die Leitung an. Mit der alten Implementierung ist dieser Test rot.
    /// </summary>
    [Fact]
    public void ReadStrings_liest_Werte_die_als_Bytes_ankommen()
    {
        var attribute = new DirectoryAttribute("memberOf",
            Encoding.UTF8.GetBytes(GroupA),
            Encoding.UTF8.GetBytes(GroupB));

        EntryReader.ReadStrings(attribute).Should().Equal(GroupA, GroupB);
    }

    /// <summary>Gegenprobe: als Zeichenketten übergebene Werte müssen weiter funktionieren.</summary>
    [Fact]
    public void ReadStrings_liest_Werte_die_als_Zeichenketten_ankommen()
    {
        var attribute = new DirectoryAttribute("memberOf", GroupA, GroupB);

        EntryReader.ReadStrings(attribute).Should().Equal(GroupA, GroupB);
    }

    /// <summary>
    /// Umlaute im Gruppennamen sind der Normalfall („Domänen-Benutzer"). Ein
    /// Byte-für-Byte-Zeichen-Cast statt einer UTF-8-Dekodierung fiele hier auf.
    /// </summary>
    [Fact]
    public void ReadStrings_dekodiert_Umlaute_richtig()
    {
        const string umlaut = "CN=Domänen-Benutzer,CN=Users,DC=example,DC=intern";
        var attribute = new DirectoryAttribute("memberOf", Encoding.UTF8.GetBytes(umlaut));

        EntryReader.ReadStrings(attribute).Should().Equal(umlaut);
    }

    [Fact]
    public void ReadStrings_vertraegt_ein_fehlendes_Attribut()
    {
        EntryReader.ReadStrings(null).Should().BeEmpty();
    }

    [Fact]
    public void ReadStrings_vertraegt_ein_leeres_Attribut()
    {
        EntryReader.ReadStrings(new DirectoryAttribute("memberOf")).Should().BeEmpty();
    }

    /// <summary>
    /// Ein echter Binärwert (kein darstellbarer Text) wird übersprungen statt
    /// eine Ausnahme auszulösen — sonst risse ein einzelnes seltsames Attribut
    /// die ganze Abfrage ab.
    /// </summary>
    [Fact]
    public void ReadStrings_ueberspringt_echte_Binaerwerte()
    {
        // Eine gültige SID-artige Bytefolge, die kein sinnvoller UTF-8-Text ist.
        byte[] binary = [0x01, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05, 0xFF, 0xFE];
        var attribute = new DirectoryAttribute("objectSid", binary);

        var act = () => EntryReader.ReadStrings(attribute);

        act.Should().NotThrow();
    }
}
