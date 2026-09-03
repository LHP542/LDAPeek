using LDAPeek.Models;

namespace LDAPeek.Services;

/// <summary>
/// Gliedert eine flache Gruppenliste für die Anzeige — als Verschachtelungsbaum,
/// nach Organisationspfad oder flach.
///
/// <b>Warum der Baum ohne Zusatzabfragen auskommt:</b>
/// <c>LDAP_MATCHING_RULE_IN_CHAIN</c> liefert die <i>transitive Hülle</i> der
/// Mitgliedschaften — jede Gruppe, über die geerbt wird, ist selbst im Ergebnis.
/// Nimmt man <c>memberOf</c> mit ins Attributset der Gruppen, sind damit alle
/// Kanten des Baums schon da. Es braucht also keine Abfrage pro Gruppe, nur ein
/// Attribut mehr in der einen Abfrage, die ohnehin läuft.
///
/// <b>Warum eine Gruppe trotzdem nur einmal erscheint:</b> Mitgliedschaften
/// bilden einen gerichteten Graphen, keinen Baum — dieselbe Gruppe kann über
/// mehrere Wege erreichbar sein. Jeden Weg einzeln aufzuklappen vervielfacht die
/// Einträge (in der Praxis aus 134 Gruppen schnell mehrere hundert Zeilen) und
/// macht die Ansicht genau so unübersichtlich, wie sie vorher war. Deshalb wird
/// jede Gruppe am <b>kürzesten</b> Weg eingehängt (Breitensuche von den direkten
/// Mitgliedschaften aus) und die übrigen Wege stehen als „auch über …" daneben.
/// </summary>
internal static class GroupTree
{
    private static readonly StringComparer DnComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>Text für Gruppen, deren Organisationspfad sich nicht bestimmen lässt.</summary>
    internal const string NoPathHeader = "ohne Pfadangabe";

    public static IReadOnlyList<GroupNode> Build(
        IReadOnlyList<AdGroup> groups, GroupViewMode mode, Func<AdGroup, bool> matches) => mode switch
        {
            GroupViewMode.Nesting => BuildNesting(groups, matches),
            GroupViewMode.Organization => BuildOrganization(groups, matches),
            _ => BuildFlat(groups, matches),
        };

    // ------------------------------------------------------------------
    // Verschachtelung
    // ------------------------------------------------------------------

    private static IReadOnlyList<GroupNode> BuildNesting(
        IReadOnlyList<AdGroup> groups, Func<AdGroup, bool> matches)
    {
        var byDn = new Dictionary<string, AdGroup>(DnComparer);
        foreach (var group in groups) byDn[group.DistinguishedName] = group;

        // <b>Die Richtung der Kanten:</b> In der Anzeige steht unter einer
        // Gruppe, was sie <i>einbringt</i>. Ist die Rollengruppe AP-FB42
        // Mitglied der Ressourcengruppe RG-Ablage, dann erbt jedes Mitglied von
        // AP-FB42 die Rechte von RG-Ablage — RG-Ablage gehört also unter
        // AP-FB42. Das steht in <c>AP-FB42.memberOf</c>. Die Kinder eines
        // Knotens sind damit seine eigenen memberOf-Werte, nicht die Gruppen,
        // die ihn als Mitglied führen.
        //
        // Umgekehrt gelesen ist derselbe Wert die Antwort auf „über welche
        // Gruppe komme ich hier hinein?" — das ist der Index für „auch über …".
        var waysInto = new Dictionary<string, List<AdGroup>>(DnComparer);
        foreach (var group in groups)
        {
            foreach (string target in group.MemberOfDns)
            {
                if (!byDn.ContainsKey(target)) continue;
                if (!waysInto.TryGetValue(target, out var list))
                {
                    waysInto[target] = list = [];
                }
                list.Add(group);
            }
        }

        // Wurzeln sind die selbst zugewiesenen Mitgliedschaften. Eine direkte
        // Gruppe bleibt Wurzel, auch wenn sie zusätzlich unter einer anderen
        // hinge — der direkte Weg ist die Antwort auf „wie kam das zustande?".
        var roots = groups.Where(g => g.Membership != MembershipKind.Nested).ToList();

        var placed = new HashSet<string>(roots.Select(g => g.DistinguishedName), DnComparer);
        var childrenOf = new Dictionary<string, List<AdGroup>>(DnComparer);

        // Breitensuche: jede Gruppe wird beim ersten Erreichen eingehängt, also
        // am kürzesten Weg. Das ist zugleich der Zyklenschutz — was schon hängt,
        // wird nicht erneut betrachtet.
        var queue = new Queue<AdGroup>(roots);
        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();

            foreach (string childDn in parent.MemberOfDns)
            {
                if (!byDn.TryGetValue(childDn, out var child)) continue;
                if (!placed.Add(childDn)) continue;

                if (!childrenOf.TryGetValue(parent.DistinguishedName, out var list))
                {
                    childrenOf[parent.DistinguishedName] = list = [];
                }
                list.Add(child);
                queue.Enqueue(child);
            }
        }

        // Übrig bleiben Gruppen, deren Weg nicht rekonstruierbar ist — etwa
        // weil eine Zwischengruppe in einer anderen Domäne liegt oder nicht
        // lesbar war. Sie verschwinden nicht, sondern stehen als eigene Wurzel.
        var orphans = groups.Where(g => !placed.Contains(g.DistinguishedName)).ToList();

        var result = new List<GroupNode>(roots.Count + orphans.Count);
        foreach (var group in roots.Concat(orphans))
        {
            if (BuildNode(group, childrenOf, waysInto, matches) is { } node) result.Add(node);
        }
        return result;
    }

    private static GroupNode? BuildNode(
        AdGroup group,
        Dictionary<string, List<AdGroup>> childrenOf,
        Dictionary<string, List<AdGroup>> waysInto,
        Func<AdGroup, bool> matches)
    {
        var children = new List<GroupNode>();
        if (childrenOf.TryGetValue(group.DistinguishedName, out var list))
        {
            foreach (var child in list)
            {
                if (BuildNode(child, childrenOf, waysInto, matches) is { } node) children.Add(node);
            }
        }

        bool self = matches(group);

        // Ein Knoten, der weder selbst passt noch einen Treffer unter sich hat,
        // fällt weg. Passt er nicht, trägt aber einen Treffer, bleibt er als
        // Weg dorthin stehen — gedämpft, denn gesucht war er nicht.
        if (!self && children.Count == 0) return null;

        return new GroupNode
        {
            Group = group,
            Children = children,
            IsContext = !self,
            AlsoVia = OtherWaysInto(group, childrenOf, waysInto),
        };
    }

    /// <summary>
    /// Die weiteren Gruppen, über die man ebenfalls in diese Gruppe gelangt —
    /// alle bis auf die, unter der sie im Baum hängt.
    /// </summary>
    private static IReadOnlyList<string> OtherWaysInto(
        AdGroup group,
        Dictionary<string, List<AdGroup>> childrenOf,
        Dictionary<string, List<AdGroup>> waysInto)
    {
        if (!waysInto.TryGetValue(group.DistinguishedName, out var ways)) return [];

        var others = new List<string>();
        foreach (var way in ways)
        {
            bool isTheChosenPath = childrenOf.TryGetValue(way.DistinguishedName, out var list)
                && list.Any(c => DnComparer.Equals(c.DistinguishedName, group.DistinguishedName));

            if (!isTheChosenPath) others.Add(way.DisplayName);
        }

        others.Sort(StringComparer.CurrentCultureIgnoreCase);
        return others;
    }

    // ------------------------------------------------------------------
    // Verzeichnis (OU) und flach
    // ------------------------------------------------------------------

    private static IReadOnlyList<GroupNode> BuildOrganization(
        IReadOnlyList<AdGroup> groups, Func<AdGroup, bool> matches)
    {
        var byPath = new Dictionary<string, List<GroupNode>>(StringComparer.CurrentCultureIgnoreCase);

        foreach (var group in groups.Where(matches))
        {
            string path = group.Path ?? NoPathHeader;
            if (!byPath.TryGetValue(path, out var list)) byPath[path] = list = [];
            list.Add(new GroupNode { Group = group });
        }

        return byPath
            .OrderBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(pair => new GroupNode { Header = pair.Key, Children = pair.Value })
            .ToList();
    }

    private static IReadOnlyList<GroupNode> BuildFlat(
        IReadOnlyList<AdGroup> groups, Func<AdGroup, bool> matches) =>
        groups.Where(matches)
            .Select(group => new GroupNode { Group = group, PathAsSubtitle = true })
            .ToList();
}
