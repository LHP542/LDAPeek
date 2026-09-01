using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using LDAPeek.Models;
using NLog;

namespace LDAPeek.Services;

/// <summary>Woher die Antwort kam — für die Fehlersuche im Log.</summary>
public enum AccessCheckSource
{
    /// <summary>Keine Regel hinterlegt.</summary>
    NoPolicy,

    /// <summary>Aus dem Windows-Anmeldetoken beantwortet (Normalfall, sofort).</summary>
    LogonToken,

    /// <summary>Über eine LDAP-Abfrage beantwortet (Konto frisch aufgenommen).</summary>
    Directory,

    /// <summary>Die Gruppe ließ sich nicht auflösen oder das Verzeichnis war stumm.</summary>
    Unresolved,
}

/// <summary>Ergebnis der Zugangsprüfung.</summary>
public sealed record AccessCheckResult(
    bool Granted,
    AccessCheckSource Source,
    string Account,
    string? RequiredGroup,
    string? Problem);

/// <summary>
/// Prüft beim Start, ob der angemeldete Benutzer in der geforderten Gruppe ist.
///
/// <b>Zwei Wege, in dieser Reihenfolge:</b>
///
/// 1. <b>Anmeldetoken.</b> <see cref="WindowsIdentity.Groups"/> enthält alle
///    wirksamen Gruppen inklusive verschachtelter und der Primärgruppe — vom
///    Domänencontroller bei der Anmeldung berechnet. Die Prüfung kostet
///    Mikrosekunden, funktioniert ohne Netz (zwischengespeicherte Anmeldung)
///    und ist genau das, worauf sich Windows selbst beim Rechtevergleich
///    stützt.
///
/// 2. <b>LDAP-Nachfrage</b>, nur wenn Schritt 1 verneint. Das Token wird bei
///    der Anmeldung eingefroren: wer gerade eben in die Gruppe aufgenommen
///    wurde, ist dort noch nicht drin und müsste sich sonst ab- und wieder
///    anmelden. Genau diese Rückfrage erspart die typische Supportrunde
///    „ich bin doch in der Gruppe".
///
/// Die Reihenfolge ist wichtig: Der schnelle Weg beantwortet den Normalfall,
/// die teure Abfrage läuft nur im Ausnahmefall.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AccessGate(AccessPolicy policy, IDirectoryService directory)
{
    private static readonly ILogger Log = LogManager.GetCurrentClassLogger();

    private readonly AccessPolicy _policy = policy;
    private readonly IDirectoryService _directory = directory;

    /// <summary>Der angemeldete Benutzer, wie er dem Nutzer angezeigt wird.</summary>
    public static string CurrentAccount =>
        $"{Environment.UserDomainName}\\{Environment.UserName}";

    public AccessPolicy Policy => _policy;

    /// <summary>
    /// Schnelle Prüfung allein am Anmeldetoken — <b>synchron und ohne
    /// Netzzugriff</b>. Wird beim Start gerufen, damit der Normalfall (Nutzer
    /// ist in der Gruppe) das Fenster nicht um eine LDAP-Abfrage verzögert.
    /// </summary>
    public AccessCheckResult CheckToken()
    {
        string account = CurrentAccount;

        if (!_policy.IsActive)
        {
            return new AccessCheckResult(true, AccessCheckSource.NoPolicy, account, null, null);
        }

        if (_policy.IsUnreadable)
        {
            return new AccessCheckResult(false, AccessCheckSource.Unresolved, account, null,
                _policy.ContactHint);
        }

        string required = _policy.RequiredGroup!.Trim();

        var sid = ResolveGroupSid(required);
        if (sid is null)
        {
            // Bewusst kein Durchlassen: eine Regel, die bei einem Tippfehler im
            // Gruppennamen still zur Erlaubnis wird, ist schlimmer als keine.
            Log.Error("Gruppe '{0}' konnte nicht aufgelöst werden — Zugang wird verweigert.", required);
            return new AccessCheckResult(false, AccessCheckSource.Unresolved, account, required,
                $"Die Gruppe „{required}“ ist im Verzeichnis nicht auffindbar. "
                + "Bitte die hinterlegte Zugangsregel prüfen.");
        }

        var stopwatch = Stopwatch.StartNew();
        if (IsInLogonToken(sid))
        {
            Log.Info("Zugang gewährt für {0} (Gruppe {1}, aus dem Anmeldetoken, {2} ms).",
                account, required, stopwatch.ElapsedMilliseconds);
            return new AccessCheckResult(true, AccessCheckSource.LogonToken, account, required, null);
        }

        Log.Info("Zugang laut Anmeldetoken verweigert für {0} — nicht in Gruppe '{1}'.", account, required);
        return new AccessCheckResult(false, AccessCheckSource.LogonToken, account, required, null);
    }

    /// <summary>
    /// Vollständige Prüfung: erst das Anmeldetoken, bei einem Nein zusätzlich
    /// die Rückfrage im Verzeichnis. Wirft nicht — ein Fehler ist ein „nein"
    /// mit Begründung.
    /// </summary>
    public async Task<AccessCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var quick = CheckToken();
        if (quick.Granted || quick.Source == AccessCheckSource.Unresolved) return quick;

        string account = quick.Account;
        string required = quick.RequiredGroup!;

        Log.Debug("{0} ist laut Anmeldetoken nicht in '{1}' — frage das Verzeichnis.", account, required);
        try
        {
            var sid = ResolveGroupSid(required);
            byte[]? sidBytes = sid is null ? null : SidUtil.FromStringSid(sid.Value);

            if (sidBytes is not null
                && await _directory.IsMemberOfGroupAsync(Environment.UserName, sidBytes, cancellationToken)
                    .ConfigureAwait(false))
            {
                Log.Info("Zugang gewährt für {0} (Gruppe {1}, laut Verzeichnis — das Anmeldetoken "
                         + "kannte die Mitgliedschaft noch nicht).", account, required);
                return new AccessCheckResult(true, AccessCheckSource.Directory, account, required, null);
            }
        }
        catch (Exception ex)
        {
            // Kein Netz, kein DC: Der Nutzer soll wissen, dass das Nein aus
            // einem Verbindungsproblem stammen kann und nicht aus seiner
            // Mitgliedschaft.
            Log.Warn(ex, "Rückfrage beim Verzeichnis fehlgeschlagen.");
            return new AccessCheckResult(false, AccessCheckSource.Unresolved, account, required,
                "Das Verzeichnis war nicht erreichbar — die Mitgliedschaft konnte deshalb nur anhand "
                + "der Windows-Anmeldung geprüft werden. Bitte die Netzverbindung (VPN) prüfen.");
        }

        Log.Info("Zugang verweigert für {0} — nicht in Gruppe '{1}'.", account, required);
        return quick;
    }

    /// <summary>
    /// Löst die konfigurierte Gruppe zu einer SID auf. Akzeptiert eine SID
    /// direkt oder einen Namen (<c>Gruppe</c> bzw. <c>DOMÄNE\Gruppe</c>).
    /// </summary>
    internal static SecurityIdentifier? ResolveGroupSid(string required)
    {
        try
        {
            return new SecurityIdentifier(required);
        }
        catch (ArgumentException)
        {
            // Kein SID-Format — dann als Kontoname auflösen.
        }

        try
        {
            return (SecurityIdentifier)new NTAccount(required).Translate(typeof(SecurityIdentifier));
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException)
        {
            return null;
        }
    }

    /// <summary>
    /// Steckt die Gruppe im Anmeldetoken? Deckt verschachtelte Gruppen und die
    /// Primärgruppe mit ab — der Domänencontroller hat sie bei der Anmeldung
    /// bereits aufgelöst.
    /// </summary>
    private static bool IsInLogonToken(SecurityIdentifier sid)
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.Groups?.Contains(sid) == true;
    }
}
