using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace LDAPeek.Services;

/// <summary>
/// Rechnen mit binären SIDs, wie AD sie im Attribut <c>objectSid</c> liefert.
///
/// Warum von Hand und nicht <c>System.Security.Principal.SecurityIdentifier</c>:
/// für die Anzeige täte der auch, aber wir brauchen zwei Operationen, die er
/// nicht anbietet — die Domänen-SID aus einer Konto-SID ableiten und aus
/// Domänen-SID + RID eine neue SID bauen. Genau das ist der einzige Weg, an die
/// <b>Primärgruppe</b> eines Benutzers zu kommen: sie steht nicht in
/// <c>memberOf</c>, sondern nur als RID in <c>primaryGroupID</c>.
///
/// Aufbau einer SID (MS-DTYP 2.4.2.2):
///   Byte 0     Revision (immer 1)
///   Byte 1     Anzahl SubAuthorities (max. 15)
///   Byte 2..7  IdentifierAuthority (48 Bit, BIG Endian)
///   ab Byte 8  je 4 Byte SubAuthority (LITTLE Endian)
/// Die gemischte Endianness ist keine Schlamperei hier, sondern steht so im
/// Protokoll — sie ist die häufigste Fehlerquelle beim Selberparsen.
/// </summary>
internal static class SidUtil
{
    private const int HeaderLength = 8;
    private const int SubAuthorityLength = 4;
    public const int MaxSubAuthorities = 15;

    /// <summary>
    /// Formatiert eine binäre SID als <c>S-1-5-21-…-1234</c>.
    /// Gibt <c>null</c> zurück, wenn die Bytes keine gültige SID sind — ein
    /// halb gelesenes Attribut soll die Detailansicht nicht sprengen.
    /// </summary>
    public static string? ToStringSid(ReadOnlySpan<byte> sid)
    {
        if (!IsValid(sid)) return null;

        int subCount = sid[1];
        ulong authority = 0;
        for (int i = 2; i < 8; i++)
        {
            authority = (authority << 8) | sid[i];
        }

        var sb = new StringBuilder(64);
        sb.Append("S-").Append(sid[0]).Append('-').Append(authority);
        for (int i = 0; i < subCount; i++)
        {
            uint sub = BinaryPrimitives.ReadUInt32LittleEndian(
                sid.Slice(HeaderLength + i * SubAuthorityLength, SubAuthorityLength));
            sb.Append('-').Append(sub);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Schneidet die letzte SubAuthority (die RID) ab und liefert die
    /// Domänen-SID. Ergebnis ist ein neues Array, das Original bleibt
    /// unverändert.
    /// </summary>
    public static byte[]? DomainSidOf(ReadOnlySpan<byte> accountSid)
    {
        if (!IsValid(accountSid) || accountSid[1] == 0) return null;

        int subCount = accountSid[1] - 1;
        var result = accountSid[..(HeaderLength + subCount * SubAuthorityLength)].ToArray();
        result[1] = (byte)subCount;
        return result;
    }

    /// <summary>
    /// Hängt eine RID an eine Domänen-SID an — so entsteht aus
    /// <c>primaryGroupID</c> die SID der Primärgruppe, nach der sich dann ganz
    /// normal per <c>(objectSid=…)</c> suchen lässt.
    /// </summary>
    public static byte[]? BuildGroupSid(ReadOnlySpan<byte> domainSid, uint rid)
    {
        if (!IsValid(domainSid) || domainSid[1] >= MaxSubAuthorities) return null;

        int subCount = domainSid[1] + 1;
        var result = new byte[HeaderLength + subCount * SubAuthorityLength];
        domainSid[..(HeaderLength + domainSid[1] * SubAuthorityLength)].CopyTo(result);
        result[1] = (byte)subCount;
        BinaryPrimitives.WriteUInt32LittleEndian(
            result.AsSpan(HeaderLength + domainSid[1] * SubAuthorityLength), rid);
        return result;
    }

    /// <summary>Die letzte SubAuthority — bei Konten die RID.</summary>
    public static uint? RidOf(ReadOnlySpan<byte> sid)
    {
        if (!IsValid(sid) || sid[1] == 0) return null;
        return BinaryPrimitives.ReadUInt32LittleEndian(
            sid.Slice(HeaderLength + (sid[1] - 1) * SubAuthorityLength, SubAuthorityLength));
    }

    /// <summary>
    /// Prüft Revision, SubAuthority-Anzahl und die tatsächliche Länge. Zu kurze
    /// Puffer sind der Normalfall bei kaputten oder abgeschnittenen Attributen.
    /// </summary>
    public static bool IsValid(ReadOnlySpan<byte> sid)
    {
        if (sid.Length < HeaderLength) return false;
        if (sid[0] != 1) return false;
        if (sid[1] > MaxSubAuthorities) return false;
        return sid.Length >= HeaderLength + sid[1] * SubAuthorityLength;
    }

    /// <summary>
    /// Formatiert eine binäre objectGUID. AD legt sie als rohe .NET-GUID-Bytes
    /// ab, deshalb reicht der Guid-Konstruktor — kein Byte-Tausch nötig.
    /// </summary>
    public static string? ToStringGuid(ReadOnlySpan<byte> guid) =>
        guid.Length == 16
            ? new Guid(guid).ToString("D", CultureInfo.InvariantCulture)
            : null;
}
