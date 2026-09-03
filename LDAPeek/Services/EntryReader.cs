using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;

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
    public IReadOnlyList<string> Strings(string name) => ReadStrings(_entry.Attributes[name]);

    /// <summary>
    /// Wandelt die Werte eines Attributs in Zeichenketten.
    ///
    /// <b>Die Falle:</b> <see cref="DirectoryAttribute"/> erbt von
    /// <c>CollectionBase</c>, und ein <c>foreach</c> darüber liefert die
    /// <b>Rohwerte</b> — also <c>byte[]</c>, nicht <c>string</c>. Nur der
    /// Indexer (und <c>GetValues</c>) wandeln den Wert nach UTF-8-Text um.
    /// Eine Schleife mit <c>value is string</c> ergibt deshalb still eine leere
    /// Liste: kein Fehler, keine Warnung, nur null Gruppen im Fenster.
    ///
    /// Der Indexer ist hier <c>GetValues</c> vorzuziehen, weil er echte
    /// Binärwerte als <c>byte[]</c> zurückgibt, statt an ihnen zu scheitern.
    /// Ob er dabei schon nach Text wandelt, hängt allerdings davon ab, wie das
    /// Attribut entstanden ist — deshalb dekodieren wir einen Bytewert hier
    /// selbst, statt uns darauf zu verlassen.
    /// </summary>
    internal static IReadOnlyList<string> ReadStrings(DirectoryAttribute? attribute)
    {
        if (attribute is null || attribute.Count == 0) return [];

        var result = new List<string>(attribute.Count);
        for (int i = 0; i < attribute.Count; i++)
        {
            switch (attribute[i])
            {
                case string { Length: > 0 } text:
                    result.Add(text);
                    break;
                case byte[] { Length: > 0 } bytes when TryDecodeUtf8(bytes, out string decoded):
                    result.Add(decoded);
                    break;
            }
        }
        return result;
    }

    /// <summary>
    /// UTF-8 mit Ausnahme statt Ersatzzeichen: ein echter Binärwert (objectSid,
    /// thumbnailPhoto) soll übersprungen werden, nicht als Zeichensalat in der
    /// Liste landen.
    /// </summary>
    private static bool TryDecodeUtf8(byte[] bytes, out string decoded)
    {
        try
        {
            decoded = StrictUtf8.GetString(bytes);
            return decoded.Length > 0;
        }
        catch (DecoderFallbackException)
        {
            decoded = string.Empty;
            return false;
        }
    }

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

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
