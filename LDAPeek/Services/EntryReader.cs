using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Runtime.Versioning;

namespace LDAPeek.Services;

/// <summary>
/// Typisierter Lesezugriff auf einen <see cref="SearchResultEntry"/>.
///
/// LDAP kennt keine Typen: jedes Attribut kommt als Zeichenkette oder als
/// Byte-Array zurück, und ein nicht gesetztes Attribut fehlt einfach ganz
/// (statt leer zu sein). Ohne eine Schicht wie diese besteht der halbe
/// DirectoryService aus Null- und Parse-Prüfungen, und jede vergessene davon
/// ist eine <see cref="NullReferenceException"/> beim ersten Konto, dem das
/// Attribut fehlt — also beim ersten Konto, das kein vollständig gepflegtes
/// Musterkonto ist.
/// </summary>
[SupportedOSPlatform("windows")]
internal readonly struct EntryReader(SearchResultEntry entry)
{
    private readonly SearchResultEntry _entry = entry;

    public string Dn => _entry.DistinguishedName;

    /// <summary>Erster Wert als Zeichenkette, oder null.</summary>
    public string? String(string name)
    {
        var attribute = _entry.Attributes[name];
        if (attribute is null || attribute.Count == 0) return null;

        string? value = attribute[0] as string;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Alle Werte als Zeichenketten.</summary>
    public IReadOnlyList<string> Strings(string name)
    {
        var attribute = _entry.Attributes[name];
        if (attribute is null || attribute.Count == 0) return [];

        var result = new List<string>(attribute.Count);
        foreach (object? value in attribute)
        {
            if (value is string s && s.Length > 0) result.Add(s);
        }
        return result;
    }

    /// <summary>
    /// Ganzzahl. AD liefert auch Zahlen als Text — <c>userAccountControl</c>
    /// kommt als <c>"512"</c> an, nicht als int.
    /// </summary>
    public int Int(string name, int fallback = 0) =>
        int.TryParse(String(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;

    /// <summary>
    /// 64-Bit-Ganzzahl für die FILETIME-Attribute. <c>long</c> ist Pflicht:
    /// <c>accountExpires</c> und <c>pwdLastSet</c> überschreiten den int-Bereich
    /// um Größenordnungen, und ein int-Parse liefert stillschweigend den
    /// Rückfallwert — also „nie" statt eines echten Datums.
    /// </summary>
    public long Long(string name, long fallback = 0) =>
        long.TryParse(String(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : fallback;

    /// <summary>Erster Wert als Byte-Array (objectSid, objectGUID, thumbnailPhoto).</summary>
    public byte[]? Bytes(string name)
    {
        var attribute = _entry.Attributes[name];
        if (attribute is null || attribute.Count == 0) return null;

        object[] values = attribute.GetValues(typeof(byte[]));
        return values.Length > 0 && values[0] is byte[] { Length: > 0 } bytes ? bytes : null;
    }

    /// <summary>
    /// Sucht ein Attribut, dessen Name mit <paramref name="baseName"/> beginnt,
    /// unabhängig von einem angehängten <c>;range=…</c>. Nötig, weil der DC
    /// mehrwertige Attribute jenseits von 1500 Werten unter einem anderen Namen
    /// zurückgibt — siehe <see cref="RangeRetrieval"/>.
    /// </summary>
    public bool TryFindRanged(string baseName, out string actualName, out IReadOnlyList<string> values)
    {
        foreach (string? name in _entry.Attributes.AttributeNames)
        {
            if (name is null) continue;
            RangeRetrieval.TryParse(name, out string parsedBase, out _, out _);
            if (!parsedBase.Equals(baseName, StringComparison.OrdinalIgnoreCase)) continue;

            actualName = name;
            values = Strings(name);
            return true;
        }

        actualName = baseName;
        values = [];
        return false;
    }
}
