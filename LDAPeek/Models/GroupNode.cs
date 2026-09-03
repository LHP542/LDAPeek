namespace LDAPeek.Models;

/// <summary>
/// Ein Eintrag in der gegliederten Gruppenansicht: entweder eine Gruppe oder
/// eine Überschrift (in der Verzeichnisansicht der Organisationspfad).
///
/// Bewusst ein einfaches Objekt ohne Benachrichtigungen: Die Ansicht wird bei
/// jeder Änderung von Filter oder Modus neu aufgebaut, nicht in Teilen
/// aktualisiert. Bei den hier auftretenden Größenordnungen (ein paar hundert
/// Gruppen) ist das schneller zu verstehen und nicht langsamer.
/// </summary>
public sealed class GroupNode
{
    /// <summary>Die Gruppe — <c>null</c> bei einer Überschrift.</summary>
    public AdGroup? Group { get; init; }

    /// <summary>Text der Überschrift, wenn <see cref="Group"/> <c>null</c> ist.</summary>
    public string? Header { get; init; }

    public IReadOnlyList<GroupNode> Children { get; init; } = [];

    /// <summary>
    /// Der Knoten passt selbst nicht zum Filter, steht aber im Baum, weil ein
    /// Treffer unter ihm hängt. Solche Knoten werden gedämpft dargestellt: Sie
    /// sind der Weg zum Treffer, nicht der Treffer.
    /// </summary>
    public bool IsContext { get; init; }

    /// <summary>
    /// Weitere Wege in diese Gruppe, wenn es mehr als einen gibt. Der Baum
    /// hängt die Gruppe nur einmal ein — sonst vervielfacht ein Geflecht mit
    /// mehreren Wegen die Einträge, bis die Liste unbrauchbar ist.
    /// </summary>
    public IReadOnlyList<string> AlsoVia { get; init; } = [];

    /// <summary>Zeigt die zweite Zeile den Organisationspfad, wenn keine Beschreibung da ist?</summary>
    public bool PathAsSubtitle { get; init; }

    public bool IsHeader => Group is null;

    public string Title => Group?.DisplayName ?? Header ?? string.Empty;

    /// <summary>
    /// Zweite Zeile. In der Baum- und der Verzeichnisansicht bleibt sie leer,
    /// wenn es keine Beschreibung gibt: Der Pfad stünde dort in jeder Zeile und
    /// verdoppelte die Höhe der Liste, ohne etwas zu unterscheiden.
    /// </summary>
    public string? Subtitle => Group is null
        ? null
        : Group.Description ?? (PathAsSubtitle ? Group.Path : null);

    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);

    public string AlsoViaText => AlsoVia.Count == 0
        ? string.Empty
        : $"auch über {string.Join(", ", AlsoVia)}";

    public bool HasAlsoVia => AlsoVia.Count > 0;

    /// <summary>Anzahl der Gruppen unterhalb dieses Knotens, ihn selbst nicht mitgezählt.</summary>
    public int DescendantCount
    {
        get
        {
            int count = 0;
            foreach (var child in Children) count += (child.IsHeader ? 0 : 1) + child.DescendantCount;
            return count;
        }
    }

    /// <summary>Zahl neben einer Überschrift oder einem Knoten mit Kindern.</summary>
    public string CountText => DescendantCount == 0 ? string.Empty : DescendantCount.ToString();

    public bool HasChildren => Children.Count > 0;

    /// <summary>Ausklappzustand beim Aufbau der Ansicht.</summary>
    public bool IsExpanded { get; init; } = true;

    /// <summary>Alle Gruppen dieses Teilbaums, Wurzel zuerst — für Zählungen und Tests.</summary>
    public IEnumerable<AdGroup> AllGroups()
    {
        if (Group is not null) yield return Group;
        foreach (var child in Children)
        {
            foreach (var group in child.AllGroups()) yield return group;
        }
    }
}
