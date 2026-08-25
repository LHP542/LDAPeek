using FluentAssertions;
using LDAPeek.Services;

namespace LDAPeek.Tests;

/// <summary>
/// Escaping und Filterbau. Das ist die Stelle, an der Nutzereingaben in einen
/// LDAP-Filter wandern — ein ungeprüftes Sonderzeichen macht daraus im besten
/// Fall Syntaxmüll und im schlechteren einen Filter, der etwas anderes sucht.
/// </summary>
public class LdapFilterTests
{
    [Theory]
    [InlineData("Müller", "Müller")]
    [InlineData("a*b", @"a\2ab")]
    [InlineData("a(b", @"a\28b")]
    [InlineData("a)b", @"a\29b")]
    [InlineData(@"a\b", @"a\5cb")]
    [InlineData("a/b", @"a\2fb")]
    public void Escape_kodiert_Sonderzeichen(string input, string expected) =>
        LdapFilter.Escape(input).Should().Be(expected);

    [Fact]
    public void Escape_kodiert_das_Nullzeichen()
    {
        LdapFilter.Escape("a\0b").Should().Be(@"a\00b");
    }

    [Fact]
    public void Escape_liefert_bei_leerer_Eingabe_eine_leere_Zeichenkette()
    {
        LdapFilter.Escape(null).Should().BeEmpty();
        LdapFilter.Escape(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void Escape_laesst_einen_Filter_nicht_ausbrechen()
    {
        // Der klassische Versuch, den Filter zu erweitern.
        string filter = LdapFilter.UserSearch("x)(objectClass=*");

        // Es darf keine unescapte schließende Klammer mitten im Wert stehen.
        filter.Should().Contain(@"x\29\28objectClass=\2a");
        // Die Klammerbilanz muss stimmen.
        filter.Count(c => c == '(').Should().Be(filter.Count(c => c == ')'));
    }

    [Fact]
    public void UserSearch_sucht_als_Teilstring_ueber_alle_Namensattribute()
    {
        string filter = LdapFilter.UserSearch("mueller");

        filter.Should().StartWith("(&(objectCategory=person)(objectClass=user)(|");
        filter.Should().Contain("(sAMAccountName=*mueller*)");
        filter.Should().Contain("(displayName=*mueller*)");
        filter.Should().Contain("(mail=*mueller*)");
        filter.Should().Contain("(userPrincipalName=*mueller*)");
    }

    [Fact]
    public void UserSearch_grenzt_auf_Personen_ein()
    {
        // objectClass=user allein liefert auch Computerkonten, weil "computer"
        // davon erbt — das ist der Grund für objectCategory=person.
        LdapFilter.UserSearch("test").Should().Contain("(objectCategory=person)");
    }

    [Fact]
    public void UserSearch_sucht_die_Personalnummer_exakt()
    {
        // Ein *123* über alle Personalnummern liefert reihenweise Zufallstreffer.
        string filter = LdapFilter.UserSearch("12345");

        filter.Should().Contain("(employeeID=12345)");
        filter.Should().NotContain("(employeeID=*12345*)");
    }

    [Fact]
    public void UserSearch_kann_exakt_suchen()
    {
        LdapFilter.UserSearch("abc", exact: true).Should().Contain("(sAMAccountName=abc)");
    }

    [Fact]
    public void UserSearch_ignoriert_umgebende_Leerzeichen() =>
        LdapFilter.UserSearch("  abc  ").Should().Contain("(sAMAccountName=*abc*)");

    [Fact]
    public void EscapeBinary_kodiert_jedes_Byte_zweistellig()
    {
        LdapFilter.EscapeBinary(new byte[] { 0x01, 0x05, 0x00, 0xFF })
            .Should().Be(@"\01\05\00\ff");
    }

    [Fact]
    public void BySid_baut_einen_Binaerfilter()
    {
        LdapFilter.BySid(new byte[] { 0x01, 0x02 }).Should().Be(@"(objectSid=\01\02)");
    }

    [Fact]
    public void GroupsOfMemberRecursive_nutzt_die_Matching_Rule_des_Servers()
    {
        // Ohne diese OID müsste der Client jede Gruppe einzeln nachschlagen —
        // bei tiefer Verschachtelung ein Vielfaches an Abfragen.
        string filter = LdapFilter.GroupsOfMemberRecursive("CN=Test,DC=x");

        filter.Should().Contain("member:1.2.840.113556.1.4.1941:=");
        filter.Should().Contain("(objectCategory=group)");
    }

    [Fact]
    public void GroupsOfMemberRecursive_escaped_das_Backslash_im_DN()
    {
        // AD liefert DNs mit escaptem Komma: "CN=Müller\, Hans,OU=…".
        // Der Backslash muss im Filter zu \5c werden, sonst matcht nichts.
        string filter = LdapFilter.GroupsOfMemberRecursive(@"CN=Müller\, Hans,OU=Benutzer,DC=lhp,DC=intern");

        filter.Should().Contain(@"CN=Müller\5c, Hans,OU=Benutzer,DC=lhp,DC=intern");
    }
}
