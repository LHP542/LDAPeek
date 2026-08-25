using System.Globalization;
using System.Text;
using LDAPeek.Models;

namespace LDAPeek.Services;

/// <summary>
/// Baut die Textzusammenfassung eines Kontos für die Zwischenablage.
///
/// Der eigentliche Zweck: eine Auskunft weiterreichen, ohne Screenshots zu
/// verschicken. Deshalb Klartext mit festen Beschriftungen — das lässt sich in
/// ein Ticket, eine Mail oder eine Notiz einfügen und bleibt lesbar.
/// </summary>
internal static class UserReport
{
    private const string DateFormat = "dd.MM.yyyy HH:mm";

    public static string Build(AdUser user, IReadOnlyCollection<AdGroup> groups)
    {
        var sb = new StringBuilder();

        sb.AppendLine(user.BestName);
        sb.AppendLine(new string('=', user.BestName.Length));
        sb.AppendLine();

        Line(sb, "Anmeldename", user.SamAccountName);
        Line(sb, "UPN", user.UserPrincipalName);
        Line(sb, "E-Mail", user.Mail);
        Line(sb, "Telefon", user.TelephoneNumber);
        Line(sb, "Mobil", user.Mobile);
        Line(sb, "Abteilung", user.Department);
        Line(sb, "Position", user.Title);
        Line(sb, "Vorgesetzter", user.ManagerName);
        Line(sb, "Büro", user.Office);
        Line(sb, "Personalnummer", user.EmployeeId);
        sb.AppendLine();

        Line(sb, "Status", user.StatusText);
        Line(sb, "Konto-Merkmale", user.FlagDescriptions.Count > 0
            ? string.Join(", ", user.FlagDescriptions)
            : null);
        Line(sb, "Passwort zuletzt gesetzt", Format(user.PasswordLastSet));
        Line(sb, "Passwort läuft ab", user.PasswordNeverExpires ? "nie" : Format(user.PasswordExpires));
        Line(sb, "Letzte Anmeldung", Format(user.LastLogon));
        Line(sb, "Konto läuft ab", Format(user.AccountExpires) ?? "nie");
        Line(sb, "Gesperrt seit", Format(user.LockedSince));
        Line(sb, "Angelegt", Format(user.Created));
        sb.AppendLine();

        Line(sb, "DN", user.DistinguishedName);
        Line(sb, "Pfad", user.Path);
        Line(sb, "SID", user.Sid);
        sb.AppendLine();

        sb.AppendLine(CultureInfo.CurrentCulture, $"Gruppen ({groups.Count}):");
        if (groups.Count == 0)
        {
            sb.AppendLine("  (keine)");
        }
        else
        {
            foreach (var group in groups)
            {
                sb.AppendLine(CultureInfo.CurrentCulture,
                    $"  - {group.DisplayName}  [{group.KindText}, {group.ScopeText}, {group.MembershipText}]");
            }
        }

        sb.AppendLine();
        sb.AppendLine(CultureInfo.CurrentCulture,
            $"Abgefragt mit LDAPeek am {DateTimeOffset.Now.ToString(DateFormat, CultureInfo.CurrentCulture)}");

        return sb.ToString();
    }

    /// <summary>Schreibt eine Zeile — leere Werte werden weggelassen, nicht als „—" gezeigt.</summary>
    private static void Line(StringBuilder sb, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.AppendLine(CultureInfo.CurrentCulture, $"{label,-26}: {value}");
    }

    private static string? Format(DateTimeOffset? value) =>
        value?.ToString(DateFormat, CultureInfo.CurrentCulture);
}
