using System.Diagnostics;
using System.DirectoryServices.Protocols;
using System.Net;
using System.Runtime.Versioning;
using LDAPeek.Models;
using NLog;

namespace LDAPeek.Services;

/// <summary>
/// Lesender AD-Zugriff über rohes LDAP (<c>System.DirectoryServices.Protocols</c>).
///
/// <b>Warum nicht AccountManagement/DirectorySearcher:</b> beide gehen über
/// ADSI/COM, verstecken die tatsächlich angeforderten Attribute und machen die
/// zwei Dinge umständlich, auf die es hier ankommt — Range-Retrieval bei sehr
/// vielen Mitgliedschaften und die serverseitige Auflösung verschachtelter
/// Gruppen per LDAP_MATCHING_RULE_IN_CHAIN.
///
/// <b>Threading:</b> Ein <see cref="LdapConnection"/> verträgt keine parallelen
/// Anfragen. Alle Operationen laufen deshalb serialisiert durch ein Semaphor und
/// die eigentlichen (blockierenden) Aufrufe auf einem Hintergrundthread — die
/// asynchrone Variante der Bibliothek (<c>BeginSendRequest</c>) bringt hier
/// keinen Vorteil, weil ohnehin nur eine Anfrage gleichzeitig laufen darf.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DirectoryService : IDirectoryService, IDisposable
{
    private static readonly ILogger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Seitengröße für die Paging-Steuerung. 1000 ist die Vorgabe von
    /// <c>MaxPageSize</c> in der Standard-Domänenrichtlinie; ein höherer Wert
    /// wird vom DC ohnehin gedeckelt.
    /// </summary>
    private const int PageSize = 1000;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly SettingsService _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private LdapConnection? _connection;
    private DirectoryConnectionInfo? _info;
    private bool _disposed;

    public DirectoryService(SettingsService settings) => _settings = settings;

    public DirectoryConnectionInfo? ConnectionInfo => _info;

    public void Invalidate()
    {
        _gate.Wait();
        try
        {
            _connection?.Dispose();
            _connection = null;
            _info = null;
            Log.Info("Verbindung verworfen — der nächste Zugriff baut neu auf.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DirectoryConnectionInfo> ConnectAsync(CancellationToken cancellationToken)
        => await WithConnectionAsync((_, info) => info, cancellationToken).ConfigureAwait(false);

    // ------------------------------------------------------------------
    // Suche
    // ------------------------------------------------------------------

    public Task<IReadOnlyList<UserSearchHit>> SearchUsersAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) return Task.FromResult<IReadOnlyList<UserSearchHit>>([]);

        int limit = Math.Max(1, _settings.Current.SearchLimit);

        return WithConnectionAsync<IReadOnlyList<UserSearchHit>>((connection, info) =>
        {
            var stopwatch = Stopwatch.StartNew();
            string filter = LdapFilter.UserSearch(query);
            Log.Debug("Benutzersuche: Basis={0} Filter={1} Limit={2}", info.SearchBase, filter, limit);

            var hits = new List<UserSearchHit>();
            foreach (var entry in Pages(connection, info.SearchBase, filter,
                         SearchScope.Subtree, AdAttributes.SearchResultSet, limit, cancellationToken))
            {
                var reader = new EntryReader(entry);
                hits.Add(new UserSearchHit
                {
                    DistinguishedName = reader.Dn,
                    SamAccountName = reader.String(AdAttributes.SamAccountName),
                    DisplayName = reader.String(AdAttributes.DisplayName),
                    CommonName = reader.String(AdAttributes.CommonName),
                    Mail = reader.String(AdAttributes.Mail),
                    Department = reader.String(AdAttributes.Department),
                    Title = reader.String(AdAttributes.Title),
                    Flags = AdValue.ParseAccountFlags(reader.Int(AdAttributes.UserAccountControl)),
                });
            }

            // Sortierung im Client statt per Server-Control: die Trefferzahl ist
            // durch das Limit klein, und ein sortiertes Suchergebnis vom DC
            // anzufordern kostet dort spürbar mehr als hier ein OrderBy.
            hits.Sort((a, b) => string.Compare(a.BestName, b.BestName, StringComparison.CurrentCultureIgnoreCase));

            Log.Info("Benutzersuche nach '{0}': {1} Treffer in {2} ms.",
                query, hits.Count, stopwatch.ElapsedMilliseconds);
            return hits;
        }, cancellationToken);
    }

    // ------------------------------------------------------------------
    // Zugangsprüfung
    // ------------------------------------------------------------------

    public Task<bool> IsMemberOfGroupAsync(
        string samAccountName, byte[] groupSid, CancellationToken cancellationToken)
    {
        return WithConnectionAsync((connection, info) =>
        {
            var stopwatch = Stopwatch.StartNew();

            // Erst den DN des Kontos holen — die Matching-Rule vergleicht gegen
            // den DN, nicht gegen den Anmeldenamen.
            var userRequest = new SearchRequest(info.SearchBase,
                LdapFilter.UserBySamAccountName(samAccountName),
                SearchScope.Subtree, AdAttributes.DistinguishedName);
            var userResponse = (SearchResponse)connection.SendRequest(userRequest, RequestTimeout);

            if (userResponse.Entries.Count == 0)
            {
                Log.Warn("Konto {0} im Verzeichnis nicht gefunden — Mitgliedschaft nicht prüfbar.",
                    samAccountName);
                return false;
            }

            string userDn = userResponse.Entries[0].DistinguishedName;

            var request = new SearchRequest(info.SearchBase,
                LdapFilter.GroupHasMemberRecursive(groupSid, userDn),
                SearchScope.Subtree, AdAttributes.CommonName);
            var response = (SearchResponse)connection.SendRequest(request, RequestTimeout);

            bool member = response.Entries.Count > 0;
            Log.Debug("Mitgliedschaftsprüfung für {0}: {1} ({2} ms).",
                samAccountName, member ? "Mitglied" : "kein Mitglied", stopwatch.ElapsedMilliseconds);
            return member;
        }, cancellationToken);
    }

    // ------------------------------------------------------------------
    // Benutzerdetails
    // ------------------------------------------------------------------

    public Task<AdUser?> LoadUserAsync(string distinguishedName, CancellationToken cancellationToken)
    {
        return WithConnectionAsync<AdUser?>((connection, _) =>
        {
            var stopwatch = Stopwatch.StartNew();

            // Base-Suche auf den DN: genau ein Objekt, keine Filterauswertung.
            var request = new SearchRequest(distinguishedName, LdapFilter.AnyObject(),
                SearchScope.Base, AdAttributes.UserDetailSet);
            var response = (SearchResponse)connection.SendRequest(request, RequestTimeout);

            if (response.Entries.Count == 0)
            {
                Log.Warn("Kein Objekt unter {0} gefunden.", distinguishedName);
                return null;
            }

            var entry = response.Entries[0];
            var reader = new EntryReader(entry);

            byte[]? sid = reader.Bytes(AdAttributes.ObjectSid);
            long expiryComputed = reader.Long(AdAttributes.PasswordExpiryComputed, AdValue.Never);
            long pwdLastSet = reader.Long(AdAttributes.PwdLastSet);

            var user = new AdUser
            {
                DistinguishedName = reader.Dn,
                SamAccountName = reader.String(AdAttributes.SamAccountName),
                UserPrincipalName = reader.String(AdAttributes.UserPrincipalName),
                DisplayName = reader.String(AdAttributes.DisplayName),
                CommonName = reader.String(AdAttributes.CommonName),
                GivenName = reader.String(AdAttributes.GivenName),
                Surname = reader.String(AdAttributes.Surname),
                Description = reader.String(AdAttributes.Description),
                Sid = sid is null ? null : SidUtil.ToStringSid(sid),
                ObjectGuid = reader.Bytes(AdAttributes.ObjectGuid) is { } guid
                    ? SidUtil.ToStringGuid(guid)
                    : null,

                Mail = reader.String(AdAttributes.Mail),
                TelephoneNumber = reader.String(AdAttributes.TelephoneNumber),
                Mobile = reader.String(AdAttributes.Mobile),
                IpPhone = reader.String(AdAttributes.IpPhone),
                Fax = reader.String(AdAttributes.Facsimile),

                Title = reader.String(AdAttributes.Title),
                Department = reader.String(AdAttributes.Department),
                Company = reader.String(AdAttributes.Company),
                Office = reader.String(AdAttributes.Office),
                ManagerDn = reader.String(AdAttributes.Manager),
                EmployeeId = reader.String(AdAttributes.EmployeeId),
                EmployeeType = reader.String(AdAttributes.EmployeeType),
                Street = reader.String(AdAttributes.StreetAddress),
                City = reader.String(AdAttributes.City),
                PostalCode = reader.String(AdAttributes.PostalCode),

                Flags = AdValue.ParseAccountFlags(reader.Int(AdAttributes.UserAccountControl)),
                PasswordLastSet = AdValue.FromFileTime(pwdLastSet),
                MustChangePassword = AdValue.MustChangePasswordAtNextLogon(pwdLastSet),
                PasswordExpires = AdValue.FromFileTime(expiryComputed),
                LastLogon = AdValue.FromFileTime(reader.Long(AdAttributes.LastLogonTimestamp)),
                AccountExpires = AdValue.FromFileTime(reader.Long(AdAttributes.AccountExpires)),
                LockedSince = AdValue.LockedSince(reader.Long(AdAttributes.LockoutTime)),
                BadPasswordCount = reader.Int(AdAttributes.BadPwdCount),
                LogonCount = reader.Int(AdAttributes.LogonCount),
                Created = AdValue.FromGeneralizedTime(reader.String(AdAttributes.WhenCreated)),
                Changed = AdValue.FromGeneralizedTime(reader.String(AdAttributes.WhenChanged)),

                HomeDirectory = reader.String(AdAttributes.HomeDirectory),
                HomeDrive = reader.String(AdAttributes.HomeDrive),
                LogonScript = reader.String(AdAttributes.ScriptPath),
                ProfilePath = reader.String(AdAttributes.ProfilePath),
                Photo = reader.Bytes(AdAttributes.ThumbnailPhoto),

                PrimaryGroupId = reader.Int(AdAttributes.PrimaryGroupId),
                MemberOfDns = ReadAllMemberOf(connection, reader, distinguishedName, cancellationToken),
            };

            Log.Info("Benutzer {0} geladen ({1} direkte Gruppen) in {2} ms.",
                user.SamAccountName ?? distinguishedName, user.MemberOfDns.Count, stopwatch.ElapsedMilliseconds);
            return user;
        }, cancellationToken);
    }

    /// <summary>
    /// Liest <c>memberOf</c> vollständig — auch jenseits der 1500-Werte-Grenze
    /// des Domain Controllers. Ohne die Nachschleife liefert ein Konto mit sehr
    /// vielen Mitgliedschaften <b>null</b> direkte Gruppen, und zwar ohne jede
    /// Fehlermeldung.
    /// </summary>
    private static IReadOnlyList<string> ReadAllMemberOf(
        LdapConnection connection, EntryReader firstRead, string dn, CancellationToken cancellationToken)
    {
        // Erster Versuch: das Attribut aus der ohnehin gelaufenen Detailabfrage.
        if (!firstRead.TryFindRanged(AdAttributes.MemberOf, out string actualName, out var values))
        {
            return [];
        }

        RangeRetrieval.TryParse(actualName, out _, out _, out int? upper);
        if (RangeRetrieval.IsComplete(upper))
        {
            return values;
        }

        var all = new List<string>(values);
        Log.Debug("memberOf von {0} ist gestückelt ({1}) — hole die restlichen Werte nach.", dn, actualName);

        // Nachschleife: jeweils ab dem nächsten Index bis "*", bis der DC eine
        // Antwort mit offener Obergrenze liefert.
        while (upper is { } last)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string next = RangeRetrieval.NextRequest(AdAttributes.MemberOf, last + 1);
            var request = new SearchRequest(dn, LdapFilter.AnyObject(), SearchScope.Base, next);
            var response = (SearchResponse)connection.SendRequest(request, RequestTimeout);
            if (response.Entries.Count == 0) break;

            var reader = new EntryReader(response.Entries[0]);
            if (!reader.TryFindRanged(AdAttributes.MemberOf, out string name, out var page)) break;

            all.AddRange(page);
            RangeRetrieval.TryParse(name, out _, out _, out upper);

            // Sicherheitsnetz: liefert der DC wider Erwarten dieselbe Grenze
            // erneut, würde die Schleife ewig laufen.
            if (page.Count == 0) break;
        }

        Log.Debug("memberOf von {0}: {1} Werte nach vollständigem Range-Retrieval.", dn, all.Count);
        return all;
    }

    // ------------------------------------------------------------------
    // Gruppen
    // ------------------------------------------------------------------

    public Task<IReadOnlyList<AdGroup>> LoadGroupsAsync(
        AdUser user, bool includeNested, CancellationToken cancellationToken)
    {
        return WithConnectionAsync<IReadOnlyList<AdGroup>>((connection, info) =>
        {
            var stopwatch = Stopwatch.StartNew();
            var byDn = new Dictionary<string, AdGroup>(StringComparer.OrdinalIgnoreCase);

            // 1. Verschachtelte Auflösung zuerst, wenn gewünscht: eine einzige
            //    Abfrage, die der DC serverseitig auflöst und die die Gruppen
            //    gleich mit allen Attributen zurückgibt.
            if (includeNested)
            {
                string filter = LdapFilter.GroupsOfMemberRecursive(user.DistinguishedName);
                Log.Debug("Verschachtelte Gruppen: {0}", filter);

                foreach (var entry in Pages(connection, info.SearchBase, filter,
                             SearchScope.Subtree, AdAttributes.GroupSet, int.MaxValue, cancellationToken))
                {
                    var group = ReadGroup(new EntryReader(entry), MembershipKind.Nested);
                    byDn[group.DistinguishedName] = group;
                }
            }

            // 2. Direkte Mitgliedschaften. Die DNs stehen schon im Benutzer; die
            //    Attribute holen wir nur für die, die Schritt 1 nicht geliefert
            //    hat. Alles andere wird lediglich als "direkt" umgestempelt.
            var missing = new List<string>();
            foreach (string dn in user.MemberOfDns)
            {
                if (byDn.TryGetValue(dn, out var known)) known.Membership = MembershipKind.Direct;
                else missing.Add(dn);
            }

            foreach (string dn in missing)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var group = LoadGroupByDn(connection, dn, MembershipKind.Direct);
                if (group is not null) byDn[group.DistinguishedName] = group;
            }

            // 3. Primärgruppe. Sie steht in KEINEM member/memberOf-Attribut,
            //    sondern nur als RID in primaryGroupID — ohne diesen Schritt
            //    fehlt bei jedem Konto ausgerechnet "Domänen-Benutzer".
            var primary = LoadPrimaryGroup(connection, info, user, cancellationToken);
            if (primary is not null)
            {
                // Ist sie über Schritt 1/2 schon da, gewinnt die Kennzeichnung
                // als Primärgruppe — sie ist die genauere Auskunft.
                byDn[primary.DistinguishedName] = primary;
            }

            var groups = byDn.Values.ToList();
            groups.Sort(CompareGroups);

            Log.Info("Gruppen für {0}: {1} gesamt ({2} direkt, {3} verschachtelt) in {4} ms.",
                user.SamAccountName ?? user.DistinguishedName, groups.Count,
                groups.Count(g => g.Membership == MembershipKind.Direct),
                groups.Count(g => g.Membership == MembershipKind.Nested),
                stopwatch.ElapsedMilliseconds);

            return groups;
        }, cancellationToken);
    }

    private static int CompareGroups(AdGroup a, AdGroup b)
    {
        int byKind = a.MembershipOrder.CompareTo(b.MembershipOrder);
        return byKind != 0
            ? byKind
            : string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase);
    }

    private static AdGroup? LoadGroupByDn(LdapConnection connection, string dn, MembershipKind membership)
    {
        try
        {
            var request = new SearchRequest(dn, LdapFilter.AnyObject(), SearchScope.Base, AdAttributes.GroupSet);
            var response = (SearchResponse)connection.SendRequest(request, RequestTimeout);
            return response.Entries.Count == 0
                ? null
                : ReadGroup(new EntryReader(response.Entries[0]), membership);
        }
        catch (DirectoryOperationException ex)
        {
            // Fremddomänen-DN oder fehlende Leseberechtigung auf das Objekt:
            // der Rest der Liste bleibt trotzdem verwertbar, also nur loggen.
            Log.Warn(ex, "Gruppe {0} konnte nicht gelesen werden — sie fehlt in der Liste.", dn);
            return null;
        }
    }

    private static AdGroup? LoadPrimaryGroup(
        LdapConnection connection, DirectoryConnectionInfo info, AdUser user, CancellationToken cancellationToken)
    {
        if (user.PrimaryGroupId <= 0) return null;

        cancellationToken.ThrowIfCancellationRequested();

        // Die SID des Benutzers steht nur als Zeichenkette im Modell — für das
        // Rechnen brauchen wir sie binär, also einmal gezielt nachladen.
        byte[]? userSid = ReadRawSid(connection, user.DistinguishedName);
        if (userSid is null)
        {
            Log.Warn("Ohne objectSid lässt sich die Primärgruppe von {0} nicht bestimmen.", user.DistinguishedName);
            return null;
        }

        byte[]? domainSid = SidUtil.DomainSidOf(userSid);
        byte[]? groupSid = domainSid is null
            ? null
            : SidUtil.BuildGroupSid(domainSid, (uint)user.PrimaryGroupId);
        if (groupSid is null) return null;

        var request = new SearchRequest(info.SearchBase, LdapFilter.BySid(groupSid),
            SearchScope.Subtree, AdAttributes.GroupSet);
        var response = (SearchResponse)connection.SendRequest(request, RequestTimeout);

        return response.Entries.Count == 0
            ? null
            : ReadGroup(new EntryReader(response.Entries[0]), MembershipKind.Primary);
    }

    private static byte[]? ReadRawSid(LdapConnection connection, string dn)
    {
        var request = new SearchRequest(dn, LdapFilter.AnyObject(), SearchScope.Base, AdAttributes.ObjectSid);
        var response = (SearchResponse)connection.SendRequest(request, RequestTimeout);
        return response.Entries.Count == 0
            ? null
            : new EntryReader(response.Entries[0]).Bytes(AdAttributes.ObjectSid);
    }

    private static AdGroup ReadGroup(EntryReader reader, MembershipKind membership)
    {
        int groupType = reader.Int(AdAttributes.GroupType);
        byte[]? sid = reader.Bytes(AdAttributes.ObjectSid);

        return new AdGroup
        {
            DistinguishedName = reader.Dn,
            Name = reader.String(AdAttributes.CommonName),
            SamAccountName = reader.String(AdAttributes.SamAccountName),
            Description = reader.String(AdAttributes.Description),
            Mail = reader.String(AdAttributes.Mail),
            ManagedBy = AdValue.FriendlyNameFromDn(reader.String(AdAttributes.ManagedBy)),
            Sid = sid is null ? null : SidUtil.ToStringSid(sid),
            Created = AdValue.FromGeneralizedTime(reader.String(AdAttributes.WhenCreated)),
            Scope = AdValue.ParseGroupScope(groupType),
            Kind = AdValue.ParseGroupKind(groupType),
            Membership = membership,
        };
    }

    // ------------------------------------------------------------------
    // Verbindung und Paging
    // ------------------------------------------------------------------

    /// <summary>
    /// Führt eine gepagte Suche aus und liefert die Einträge Seite für Seite.
    /// Ohne die Paging-Steuerung gibt der DC bei mehr als <c>MaxPageSize</c>
    /// Treffern einen Fehler statt einer gekürzten Liste zurück.
    /// </summary>
    private static IEnumerable<SearchResultEntry> Pages(
        LdapConnection connection, string baseDn, string filter, SearchScope scope,
        string[] attributes, int limit, CancellationToken cancellationToken)
    {
        var paging = new PageResultRequestControl(PageSize);
        int returned = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var request = new SearchRequest(baseDn, filter, scope, attributes);
            request.Controls.Add(paging);

            var response = (SearchResponse)connection.SendRequest(request, RequestTimeout);

            foreach (SearchResultEntry entry in response.Entries)
            {
                yield return entry;
                if (++returned >= limit) yield break;
            }

            var cookie = response.Controls
                .OfType<PageResultResponseControl>()
                .FirstOrDefault()?.Cookie;

            // Leerer Cookie = letzte Seite.
            if (cookie is null || cookie.Length == 0) yield break;
            paging.Cookie = cookie;
        }
    }

    /// <summary>
    /// Stellt sicher, dass eine gebundene Verbindung existiert, und führt
    /// <paramref name="work"/> serialisiert auf einem Hintergrundthread aus.
    /// </summary>
    private async Task<T> WithConnectionAsync<T>(
        Func<LdapConnection, DirectoryConnectionInfo, T> work, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                if (_connection is null || _info is null) OpenConnection();
                return work(_connection!, _info!);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (LdapException ex)
        {
            // Abgelaufene oder abgerissene Sitzung: die nächste Anfrage soll neu
            // binden, statt am toten Handle hängen zu bleiben.
            Log.Error(ex, "LDAP-Fehler (Code {0}) — Verbindung wird verworfen.", ex.ErrorCode);
            _connection?.Dispose();
            _connection = null;
            _info = null;
            throw new DirectoryAccessException(DescribeLdapError(ex), ex);
        }
        catch (DirectoryOperationException ex)
        {
            Log.Error(ex, "LDAP-Operation abgelehnt: {0}", ex.Response?.ErrorMessage);
            throw new DirectoryAccessException(
                ex.Response?.ErrorMessage is { Length: > 0 } message
                    ? $"Das Verzeichnis hat die Anfrage abgelehnt: {message}"
                    : "Das Verzeichnis hat die Anfrage abgelehnt.", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Baut die Verbindung auf und liest den RootDSE. Läuft unter dem Semaphor.</summary>
    private void OpenConnection()
    {
        var settings = _settings.Current;
        string target = string.IsNullOrWhiteSpace(settings.Server)
            ? DefaultDomain()
            : settings.Server.Trim();

        int port = settings.UseTls && settings.Port == 389 ? 636 : settings.Port;
        var identifier = new LdapDirectoryIdentifier(target, port, fullyQualifiedDnsHostName: false, connectionless: false);

        string? password = SecretProtection.Unprotect(settings.BindPasswordProtected);
        NetworkCredential? credential = null;
        string boundAs = $"{Environment.UserDomainName}\\{Environment.UserName}";

        if (!string.IsNullOrWhiteSpace(settings.BindUser))
        {
            credential = ParseCredential(settings.BindUser.Trim(), password);
            boundAs = settings.BindUser.Trim();
        }

        var connection = new LdapConnection(identifier, credential, AuthType.Negotiate)
        {
            Timeout = RequestTimeout,
            AutoBind = false,
        };
        connection.SessionOptions.ProtocolVersion = 3;

        // Ohne diese Zeile folgt der Client Verweisen in andere Domänen. Bei
        // einer Suche über die ganze Gesamtstruktur wird daraus schnell ein
        // Aufruf, der Minuten braucht oder in einen Timeout läuft.
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;

        if (settings.UseTls)
        {
            connection.SessionOptions.SecureSocketLayer = true;
        }
        else
        {
            // Kerberos-Signierung und -Verschlüsselung. Das ist der Grund, warum
            // Port 389 hier vertretbar ist: die Sitzung ist trotzdem verschlüsselt,
            // und viele DCs erzwingen Signierung ohnehin (LdapEnforceChannelBinding).
            // Mit SSL darf beides NICHT gesetzt werden, sonst schlägt der Bind fehl.
            connection.SessionOptions.Signing = true;
            connection.SessionOptions.Sealing = true;
        }

        var stopwatch = Stopwatch.StartNew();
        connection.Bind();
        Log.Info("Bind gegen {0}:{1} als {2} erfolgreich ({3} ms).",
            target, port, boundAs, stopwatch.ElapsedMilliseconds);

        var rootDse = ReadRootDse(connection);
        string searchBase = !string.IsNullOrWhiteSpace(settings.SearchBase)
            ? settings.SearchBase.Trim()
            : rootDse.DefaultNamingContext
              ?? throw new DirectoryAccessException(
                  "Der Server hat keinen defaultNamingContext gemeldet. Bitte die Such-Basis in den Einstellungen von Hand setzen.");

        _connection = connection;
        _info = new DirectoryConnectionInfo
        {
            Target = target,
            DomainController = rootDse.DnsHostName,
            SearchBase = searchBase,
            BoundAs = boundAs,
            IsTls = settings.UseTls,
        };

        Log.Info("Verbunden: {0}", _info.StatusText);
    }

    private static (string? DefaultNamingContext, string? DnsHostName) ReadRootDse(LdapConnection connection)
    {
        // Der RootDSE liegt unter dem leeren DN und ist ohne Suchbasis lesbar.
        var request = new SearchRequest(string.Empty, LdapFilter.AnyObject(), SearchScope.Base,
            AdAttributes.DefaultNamingContext, AdAttributes.DnsHostName);
        var response = (SearchResponse)connection.SendRequest(request, RequestTimeout);

        if (response.Entries.Count == 0) return (null, null);

        var reader = new EntryReader(response.Entries[0]);
        return (reader.String(AdAttributes.DefaultNamingContext), reader.String(AdAttributes.DnsHostName));
    }

    /// <summary>
    /// Zerlegt <c>DOMÄNE\benutzer</c> oder <c>benutzer@domäne</c> in ein
    /// <see cref="NetworkCredential"/>. Wird das Domänenteil weggelassen,
    /// übernimmt Windows die Anmeldedomäne.
    /// </summary>
    internal static NetworkCredential ParseCredential(string user, string? password)
    {
        int backslash = user.IndexOf('\\');
        if (backslash > 0)
        {
            return new NetworkCredential(user[(backslash + 1)..], password, user[..backslash]);
        }

        int at = user.IndexOf('@');
        return at > 0
            ? new NetworkCredential(user[..at], password, user[(at + 1)..])
            : new NetworkCredential(user, password);
    }

    /// <summary>
    /// Standarddomäne des angemeldeten Benutzers. <c>USERDNSDOMAIN</c> ist der
    /// DNS-Name (<c>lhp.intern</c>) — den braucht der DC-Locator; der
    /// NetBIOS-Name aus <c>USERDOMAIN</c> taugt dafür nur als Notnagel.
    /// </summary>
    internal static string DefaultDomain()
    {
        string? dns = Environment.GetEnvironmentVariable("USERDNSDOMAIN");
        if (!string.IsNullOrWhiteSpace(dns)) return dns;

        string? netbios = Environment.GetEnvironmentVariable("USERDOMAIN");
        if (!string.IsNullOrWhiteSpace(netbios)) return netbios;

        throw new DirectoryAccessException(
            "Der Rechner scheint nicht an einer Domäne angemeldet zu sein. "
            + "Bitte in den Einstellungen einen Server eintragen.");
    }

    /// <summary>
    /// Übersetzt die häufigsten LDAP-Fehlercodes in einen Satz, der dem Nutzer
    /// weiterhilft. Ein nacktes „LdapException 81" tut das nicht.
    /// </summary>
    private static string DescribeLdapError(LdapException ex) => ex.ErrorCode switch
    {
        49 => "Anmeldung abgelehnt. Bitte Benutzername und Passwort in den Einstellungen prüfen.",
        81 => "Der Verzeichnisserver ist nicht erreichbar. Besteht eine Verbindung ins Firmennetz (VPN)?",
        85 => "Zeitüberschreitung beim Verzeichnisserver. Bitte erneut versuchen oder die Such-Basis eingrenzen.",
        50 => "Keine ausreichenden Rechte für diese Abfrage.",
        _ => $"Zugriff auf das Verzeichnis fehlgeschlagen (LDAP-Fehler {ex.ErrorCode}: {ex.Message}).",
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection?.Dispose();
        _gate.Dispose();
    }
}

/// <summary>
/// Fehler beim Verzeichniszugriff mit einer Meldung, die im UI angezeigt werden
/// darf — die technischen Details stehen im Log, nicht im Dialog.
/// </summary>
public sealed class DirectoryAccessException(string message, Exception? inner = null)
    : Exception(message, inner);
