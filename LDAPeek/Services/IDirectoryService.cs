using LDAPeek.Models;

namespace LDAPeek.Services;

/// <summary>
/// Lesender Zugriff auf Active Directory. Bewusst als Schnittstelle, damit die
/// ViewModels ohne laufende Domäne testbar bleiben — und damit im Code klar
/// sichtbar ist, dass es hier keine schreibende Operation gibt.
/// </summary>
public interface IDirectoryService
{
    /// <summary>Aktuelle Verbindung, oder null solange nicht gebunden wurde.</summary>
    DirectoryConnectionInfo? ConnectionInfo { get; }

    /// <summary>
    /// Baut die Verbindung auf und liest den RootDSE. Ist bereits verbunden,
    /// wird die bestehende Verbindung zurückgegeben.
    /// </summary>
    Task<DirectoryConnectionInfo> ConnectAsync(CancellationToken cancellationToken);

    /// <summary>Sucht Benutzerkonten über mehrere Attribute.</summary>
    Task<IReadOnlyList<UserSearchHit>> SearchUsersAsync(string query, CancellationToken cancellationToken);

    /// <summary>Lädt alle Detailattribute eines Kontos anhand seines DN.</summary>
    Task<AdUser?> LoadUserAsync(string distinguishedName, CancellationToken cancellationToken);

    /// <summary>
    /// Ermittelt die Gruppenmitgliedschaften: Primärgruppe, direkte Gruppen und
    /// — falls <paramref name="includeNested"/> gesetzt ist — die über
    /// Verschachtelung geerbten.
    /// </summary>
    Task<IReadOnlyList<AdGroup>> LoadGroupsAsync(
        AdUser user, bool includeNested, CancellationToken cancellationToken);

    /// <summary>
    /// Verwirft die bestehende Verbindung. Nach einer Änderung an Server, Port
    /// oder Anmeldedaten Pflicht, sonst arbeitet das Werkzeug weiter gegen die
    /// alte Sitzung und die neue Einstellung wirkt scheinbar nicht.
    /// </summary>
    void Invalidate();
}
