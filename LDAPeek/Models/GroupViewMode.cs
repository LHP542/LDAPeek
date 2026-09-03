namespace LDAPeek.Models;

/// <summary>
/// Wie die Gruppenliste gegliedert wird. Drei Ansichten, weil es drei
/// verschiedene Fragen sind: „Woher kommt das Recht?" (Verschachtelung),
/// „Wer verwaltet die Gruppe?" (Verzeichnis) und „Ist X dabei?" (flach).
/// </summary>
public enum GroupViewMode
{
    /// <summary>Baum entlang der Gruppenverschachtelung — direkte Gruppen als Wurzeln.</summary>
    Nesting,

    /// <summary>Nach dem Organisationspfad der Gruppe gruppiert (OU).</summary>
    Organization,

    /// <summary>Eine flache, alphabetische Liste — die bisherige Ansicht.</summary>
    Flat,
}
