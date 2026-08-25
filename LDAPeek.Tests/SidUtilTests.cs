using FluentAssertions;
using LDAPeek.Services;

namespace LDAPeek.Tests;

/// <summary>
/// SID-Arithmetik. Die gemischte Endianness (Authority big, SubAuthorities
/// little) ist die häufigste Fehlerquelle beim Selberparsen — und ohne
/// funktionierende Domänen-SID-Ableitung fehlt bei jedem Konto die
/// Primärgruppe, typischerweise „Domänen-Benutzer".
/// </summary>
public class SidUtilTests
{
    /// <summary>
    /// Baut eine SID aus Revision, Authority und SubAuthorities — bewusst hier
    /// im Test von Hand statt als Byte-Literal, damit die Erwartung lesbar
    /// bleibt und nicht selbst zur Fehlerquelle wird.
    /// </summary>
    private static byte[] BuildSid(ulong authority, params uint[] subAuthorities)
    {
        var sid = new byte[8 + subAuthorities.Length * 4];
        sid[0] = 1;
        sid[1] = (byte)subAuthorities.Length;

        // IdentifierAuthority: 48 Bit BIG Endian.
        for (int i = 0; i < 6; i++) sid[2 + i] = (byte)(authority >> (8 * (5 - i)));

        // SubAuthorities: je 32 Bit LITTLE Endian.
        for (int i = 0; i < subAuthorities.Length; i++)
        {
            BitConverter.GetBytes(subAuthorities[i]).CopyTo(sid, 8 + i * 4);
        }
        return sid;
    }

    /// <summary>S-1-5-21-1004336348-1177238915-682003330-512 (Domänen-Admins).</summary>
    private static byte[] DomainAdminsSid() =>
        BuildSid(5, 21, 1004336348, 1177238915, 682003330, 512);

    [Fact]
    public void ToStringSid_formatiert_eine_Domaenen_SID()
    {
        SidUtil.ToStringSid(DomainAdminsSid())
            .Should().Be("S-1-5-21-1004336348-1177238915-682003330-512");
    }

    [Fact]
    public void ToStringSid_liest_die_Authority_als_Big_Endian()
    {
        // S-1-5-32-544 (Vordefiniert\Administratoren). Die Authority steht
        // big endian in den Bytes 2..7 — würde man sie little endian lesen,
        // käme hier eine absurd große Zahl heraus.
        SidUtil.ToStringSid(BuildSid(5, 32, 544)).Should().Be("S-1-5-32-544");
    }

    [Fact]
    public void RidOf_liefert_die_letzte_SubAuthority() =>
        SidUtil.RidOf(DomainAdminsSid()).Should().Be(512u);

    [Fact]
    public void DomainSidOf_schneidet_die_RID_ab()
    {
        byte[]? domain = SidUtil.DomainSidOf(DomainAdminsSid());

        SidUtil.ToStringSid(domain).Should().Be("S-1-5-21-1004336348-1177238915-682003330");
        domain!.Length.Should().Be(8 + 4 * 4);
    }

    [Fact]
    public void DomainSidOf_laesst_das_Original_unveraendert()
    {
        byte[] original = DomainAdminsSid();
        byte[] copy = [.. original];

        SidUtil.DomainSidOf(original);

        original.Should().Equal(copy);
    }

    [Fact]
    public void BuildGroupSid_haengt_die_RID_wieder_an()
    {
        // Genau dieser Weg führt von primaryGroupID=513 zur SID von
        // "Domänen-Benutzer".
        byte[]? domain = SidUtil.DomainSidOf(DomainAdminsSid());
        byte[]? group = SidUtil.BuildGroupSid(domain, 513);

        SidUtil.ToStringSid(group)
            .Should().Be("S-1-5-21-1004336348-1177238915-682003330-513");
    }

    [Fact]
    public void BuildGroupSid_ist_die_Umkehrung_von_DomainSidOf()
    {
        byte[] original = DomainAdminsSid();
        byte[]? rebuilt = SidUtil.BuildGroupSid(SidUtil.DomainSidOf(original), SidUtil.RidOf(original)!.Value);

        rebuilt.Should().Equal(original);
    }

    [Fact]
    public void BuildGroupSid_lehnt_eine_volle_SubAuthority_Liste_ab()
    {
        byte[] full = new byte[8 + 15 * 4];
        full[0] = 1;
        full[1] = SidUtil.MaxSubAuthorities;

        SidUtil.BuildGroupSid(full, 513).Should().BeNull();
    }

    [Theory]
    [InlineData(new byte[0])]                                              // leer
    [InlineData(new byte[] { 0x01, 0x02, 0x00 })]                          // zu kurz
    [InlineData(new byte[] { 0x02, 0x01, 0, 0, 0, 0, 0, 5, 1, 0, 0, 0 })]  // falsche Revision
    public void IsValid_erkennt_kaputte_Puffer(byte[] sid) =>
        SidUtil.IsValid(sid).Should().BeFalse();

    [Fact]
    public void IsValid_erkennt_eine_abgeschnittene_SID()
    {
        // Der Header verspricht 5 SubAuthorities, es folgen aber nur zwei —
        // genau das passiert bei einem abgeschnittenen Attributwert.
        byte[] truncated = [.. BuildSid(5, 21, 1).Take(16)];
        truncated[1] = 5;

        SidUtil.IsValid(truncated).Should().BeFalse();
        SidUtil.ToStringSid(truncated).Should().BeNull();
        SidUtil.DomainSidOf(truncated).Should().BeNull();
        SidUtil.RidOf(truncated).Should().BeNull();
    }

    [Fact]
    public void ToStringGuid_formatiert_eine_objectGUID()
    {
        var guid = Guid.NewGuid();

        SidUtil.ToStringGuid(guid.ToByteArray()).Should().Be(guid.ToString("D"));
    }

    [Fact]
    public void ToStringGuid_lehnt_falsche_Laengen_ab() =>
        SidUtil.ToStringGuid(new byte[15]).Should().BeNull();
}
