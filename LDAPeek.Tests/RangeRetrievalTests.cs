using FluentAssertions;
using LDAPeek.Services;

namespace LDAPeek.Tests;

/// <summary>
/// Range-Retrieval. Der Fehler, den diese Tests absichern, ist besonders
/// unangenehm: bei einem Konto mit über 1500 Mitgliedschaften liefert der DC
/// das Attribut unter einem <b>anderen Namen</b> zurück. Wer nur auf
/// <c>memberOf</c> prüft, bekommt eine leere Liste — ohne Fehler, ohne Warnung.
/// Und es fällt erst bei dem Konto auf, bei dem jemand eine verlässliche
/// Auskunft braucht.
/// </summary>
public class RangeRetrievalTests
{
    [Fact]
    public void TryParse_erkennt_ein_Attribut_ohne_Range()
    {
        RangeRetrieval.TryParse("memberOf", out string baseName, out int lower, out int? upper)
            .Should().BeFalse();

        baseName.Should().Be("memberOf");
        lower.Should().Be(0);
        upper.Should().BeNull();
    }

    [Fact]
    public void TryParse_liest_eine_begrenzte_Seite()
    {
        RangeRetrieval.TryParse("memberOf;range=0-1499", out string baseName, out int lower, out int? upper)
            .Should().BeTrue();

        baseName.Should().Be("memberOf");
        lower.Should().Be(0);
        upper.Should().Be(1499);
        RangeRetrieval.IsComplete(upper).Should().BeFalse();
    }

    [Fact]
    public void TryParse_erkennt_die_letzte_Seite_am_Stern()
    {
        RangeRetrieval.TryParse("memberOf;range=1500-*", out string baseName, out int lower, out int? upper)
            .Should().BeTrue();

        baseName.Should().Be("memberOf");
        lower.Should().Be(1500);
        upper.Should().BeNull();
        RangeRetrieval.IsComplete(upper).Should().BeTrue();
    }

    [Fact]
    public void TryParse_ist_gegenueber_der_Schreibweise_tolerant()
    {
        // Der Server bestimmt die Groß-/Kleinschreibung, nicht wir.
        RangeRetrieval.TryParse("memberof;RANGE=0-99", out string baseName, out _, out int? upper)
            .Should().BeTrue();

        baseName.Should().Be("memberof");
        upper.Should().Be(99);
    }

    [Theory]
    [InlineData("memberOf;range=")]
    [InlineData("memberOf;range=abc")]
    [InlineData("memberOf;range=0")]
    [InlineData("memberOf;range=x-9")]
    [InlineData("memberOf;range=0-x")]
    public void TryParse_meldet_kaputte_Range_Angaben(string attributeName) =>
        RangeRetrieval.TryParse(attributeName, out _, out _, out _).Should().BeFalse();

    [Fact]
    public void FirstRequest_fordert_ab_null_alles_an() =>
        RangeRetrieval.FirstRequest("memberOf").Should().Be("memberOf;range=0-*");

    [Fact]
    public void NextRequest_setzt_hinter_der_letzten_Seite_an()
    {
        // Auf "memberOf;range=0-1499" folgt "memberOf;range=1500-*".
        RangeRetrieval.TryParse("memberOf;range=0-1499", out string baseName, out _, out int? upper);

        RangeRetrieval.NextRequest(baseName, upper!.Value + 1)
            .Should().Be("memberOf;range=1500-*");
    }

    [Fact]
    public void Der_Rundlauf_ueber_mehrere_Seiten_endet_am_Stern()
    {
        // Simuliert, was der DC bei 3200 Mitgliedschaften antwortet.
        string[] serverAnswers =
        [
            "memberOf;range=0-1499",
            "memberOf;range=1500-2999",
            "memberOf;range=3000-*",
        ];

        var requested = new List<string>();
        int? upper = -1;
        int round = 0;

        while (upper is { } last && round < serverAnswers.Length)
        {
            requested.Add(RangeRetrieval.NextRequest("memberOf", last + 1));
            RangeRetrieval.TryParse(serverAnswers[round], out _, out _, out upper);
            round++;
        }

        requested.Should().Equal("memberOf;range=0-*", "memberOf;range=1500-*", "memberOf;range=3000-*");
        RangeRetrieval.IsComplete(upper).Should().BeTrue();
    }
}
