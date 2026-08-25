# LDAPeek

[![CI](https://github.com/Kroste/LDAPeek/actions/workflows/ci.yml/badge.svg)](https://github.com/Kroste/LDAPeek/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/Kroste/LDAPeek)](https://github.com/Kroste/LDAPeek/releases)

Active-Directory-Konten nachschlagen: Stammdaten, Kontozustand und vor allem die
**Gruppenmitgliedschaften — inklusive der über Verschachtelung geerbten**.

LDAPeek ist ein **rein lesendes** Werkzeug. Es schreibt nichts ins Verzeichnis,
und es gibt keinen Codepfad, der das könnte.

![LDAPeek](LDAPeek/Assets/ldapeek.png)

## Wofür

Die Frage „warum hat die Kollegin Zugriff auf X?" lässt sich mit der flachen
Gruppenliste aus *Active Directory-Benutzer und -Computer* nicht beantworten:
dort steht nur, was dem Konto **direkt** zugewiesen wurde. In einer gewachsenen
Domäne ist das die Minderheit. Ein Beispiel aus dem Alltag: 33 direkte
Mitgliedschaften, aber 132 effektive — 99 davon geerbt.

LDAPeek zeigt beides nebeneinander und markiert bei jeder Gruppe, wie das Konto
hineingekommen ist:

| Kennzeichnung  | Bedeutung |
|----------------|-----------|
| **Primärgruppe** | Aus `primaryGroupID` — typisch „Domänen-Benutzer". Steht in **keinem** `memberOf` und fehlt deshalb in vielen selbstgebauten Auswertungen. |
| **direkt**       | Steht im `memberOf` des Kontos. |
| **verschachtelt**| Geerbt über eine andere Gruppe. Der Domain Controller löst das serverseitig auf. |

## Features

- **Suche über mehrere Attribute** — Anmeldename, Anzeigename, Vor- und
  Nachname, E-Mail, Benutzerprinzipalname; Personalnummern werden exakt gesucht.
  Genau ein Treffer wird direkt geöffnet.
- **Vollständige Stammdaten** — Organisation, Erreichbarkeit, Anschrift,
  Verzeichnisangaben (DN, SID, Objekt-GUID), Profilpfade und das im Verzeichnis
  hinterlegte Foto.
- **Kontozustand im Klartext** — aktiv/deaktiviert/gesperrt/abgelaufen, dazu die
  Merkmale aus `userAccountControl` („Passwort läuft nie ab", „Smartcard
  erforderlich", …). Zeitangaben immer mit Abstand zu heute: *14.03.2026 08:12
  (vor 12 Tagen)*.
- **Gruppen filtern** — Volltextfilter über Name, Anmeldename und Beschreibung,
  optional nur Sicherheitsgruppen (Verteilergruppen tragen keine Berechtigungen).
- **Weitergeben** — Gruppenliste als CSV in die Zwischenablage oder als Datei,
  oder eine vollständige Textzusammenfassung des Kontos für ein Ticket.
- **Update über den Netzwerkordner** — kein Internetzugang nötig.

## Installation

Es gibt keine Installation. Das ZIP von der
[Releases-Seite](https://github.com/Kroste/LDAPeek/releases) oder aus
`\\samba01\542$\5424_IT-Basis-Dienste\LDAPeek` entpacken und `LDAPeek.exe`
starten. Das Paket ist self-contained — .NET muss nicht installiert sein.

**Voraussetzungen:** Windows, Rechner an der Domäne angemeldet (oder VPN).
Es genügen die normalen Leserechte, die jedes Domänenkonto im Verzeichnis hat.

## Bedienung

1. Links einen Suchbegriff eintippen und **Suchen** (oder Eingabetaste). Ab zwei
   Zeichen; zuletzt gesuchte Begriffe schlägt das Feld selbst vor.
2. Einen Treffer anklicken. Rechts oben stehen die Stammdaten, darunter die
   Gruppen.
3. Die Fuge zwischen Stammdaten und Gruppen lässt sich ziehen — wer vor allem
   Gruppen prüft, zieht sie nach oben.

| Taste | Wirkung |
|-------|---------|
| `Strg`+`F` | Cursor ins Suchfeld |
| `F5`       | Konto und Gruppen neu abfragen |
| `Esc`      | Suchfeld leeren |

**Minimieren legt LDAPeek in den Infobereich** (Tray), Schließen beendet es. Ein
zweiter Start holt das laufende Fenster nach vorn, statt ein zweites zu öffnen.

### Verschachtelte Gruppen

Der Schalter **Verschachtelte auflösen** steuert, ob geerbte Mitgliedschaften
mitgeholt werden. Er ist standardmäßig an, weil das die interessante Auskunft
ist. Die Abfrage läuft serverseitig
(`LDAP_MATCHING_RULE_IN_CHAIN`) und dauert je nach Verschachtelungstiefe ein
paar Sekunden — bei einem Konto mit 132 effektiven Gruppen etwa vier. Wer nur
schnell die direkten Zuweisungen sehen will, schaltet ihn aus.

## Einstellungen

Über das Zahnrad in der Titelleiste. Alles ist optional — ohne eine einzige
Einstellung findet LDAPeek die Anmeldedomäne selbst.

| Einstellung | Bedeutung |
|-------------|-----------|
| **Domäne oder Domain Controller** | Leer = Anmeldedomäne des angemeldeten Benutzers. Ein konkreter DC ist nur nötig, wenn ein bestimmter geprüft werden soll. |
| **Port / LDAPS** | Standard 389. Auch ohne LDAPS ist die Sitzung verschlüsselt: der Kerberos-Bind wird signiert und versiegelt. |
| **Such-Basis** | Leer = ganze Domäne. Eine einzelne OU beschleunigt die Suche spürbar. |
| **Benutzerkonto / Passwort** | Leer = angemeldetes Windows-Konto. Nur nötig, wenn das Alltagskonto nicht lesen darf. Das Passwort wird per DPAPI an das Windows-Konto gebunden gespeichert, nie im Klartext. |
| **Höchstzahl Treffer** | Standard 200. |
| **Update-Ordner** | Netzwerkordner mit den Paketen. |

Die Konfiguration liegt unter `%LOCALAPPDATA%\LDAPeek\settings.json`.

## Updates

LDAPeek sieht beim Start im Update-Ordner nach und meldet eine neuere Version
als Hinweiszeile. Installiert wird sie über **ⓘ → Auf Updates prüfen →
Update installieren**: LDAPeek kopiert das Paket lokal, entpackt es, tauscht sich
selbst aus und startet neu.

**Ausrollen** ist ein Kopiervorgang: das ZIP unverändert nach
`\\samba01\542$\5424_IT-Basis-Dienste\LDAPeek` legen. Der Dateiname trägt die
Version (`LDAPeek-1.2.0-win-x64.zip`), und es gewinnt immer die **höchste**
Version — nicht die zuletzt kopierte Datei. Ein zurückkopiertes altes Paket
löst also kein „Update" nach unten aus. Ist der Ordner nicht erreichbar
(Notebook ohne Netz), passiert schlicht nichts.

## Logs und Fehlersuche

Logdateien liegen unter `%LOCALAPPDATA%\LDAPeek\logs\` (Tagesarchiv, 14 Tage)
und sind über **ⓘ → Logdateien öffnen** erreichbar. LDAPeek protokolliert jede
Abfrage samt Filter und Dauer — bei einer überraschenden Auskunft steht dort,
welcher Domain Controller sie geliefert hat. Passwörter und Tokens werden
automatisch maskiert.

Häufige Meldungen:

| Meldung | Ursache |
|---------|---------|
| *Der Verzeichnisserver ist nicht erreichbar* | Keine Verbindung ins Firmennetz — VPN prüfen. |
| *Anmeldung abgelehnt* | Das in den Einstellungen hinterlegte Konto oder Passwort stimmt nicht. |
| *Zeitüberschreitung* | Suche zu breit — Such-Basis auf eine OU eingrenzen. |

## Entwicklung

```bash
dotnet build   # bauen
dotnet test    # Tests
dotnet run --project LDAPeek
```

Release: VS-Code-Task „release (tag + push)" — prüft den Git-Zustand, setzt den
Tag und stößt die GitHub-Action an, die das ZIP baut.

Details zu Architektur und Fallstricken: [CLAUDE.md](CLAUDE.md).

## Lizenz

MIT — siehe [LICENSE](LICENSE).
