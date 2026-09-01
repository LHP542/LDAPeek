# LDAPeek

## Grundlagen

- **Was:** Rein lesendes Windows-Werkzeug, das AD-Benutzerkonten nachschlägt und
  ihre Gruppenmitgliedschaften zeigt — direkte, geerbte und die Primärgruppe.
- **Stack:** C# / .NET 10 (`net10.0-windows`) / Avalonia 12.1,
  CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, NLog (mit
  Secret-Masking), xunit.v3 + FluentAssertions 7.x.
- **Struktur:** Flach (kein `src/`), `.slnx`, Central Package Management,
  `Directory.Build.props`, MinVer (Tags `v*`).
- **Repo:** `github.com/LHP542/LDAPeek`.
- **Kommunikation:** Deutsch, „du". Lars entwirft, Claude implementiert.

### Bewusste Abweichungen vom Kroste-Standard

Alle drei sind Vorgaben von Lars beim Projektstart (2026-08-25) und **keine
Nachlässigkeit** — nicht ungefragt „nachziehen":

1. **Windows-only** statt cross-platform. `System.DirectoryServices.Protocols`
   mit Kerberos-Bind und DPAPI gibt es sinnvoll nur hier; ein Linux-Build wäre
   eine Attrappe. Deshalb `net10.0-windows`, kein AppImage, kein tar.gz, CI auf
   `windows-latest`.
2. **Nur Deutsch**, keine EN+DE-Lokalisierung. Dienstliches Werkzeug für einen
   Fachbereich; alle Nutzer sprechen Deutsch. Es gibt kein `Localization/` und
   keine Resx — wenn das je gebraucht wird, ist es eine bewusste Erweiterung.
3. **Update über einen Netzwerkordner** statt GitHub Releases — die Ausnahme,
   die `references/autoupdate.md` für dienstliche Werkzeuge im Firmennetz
   vorsieht. Kanal: `\\samba01\542$\5424_IT-Basis-Dienste\LDAPeek`.
   **LDAPeek gehört damit in die Liste dieser Ausnahmen im Skill** (bisher nur
   Checkmk Cockpit samt Plugins und DTM).

Kein BMC-Link und keine `FUNDING.yml`: dienstliches Werkzeug.

### Umgebungshinweis: nuget.org-Downloads sind geblockt

Auf dem Arbeitslaptop liefert `dotnet package search` neuere Paketversionen,
die sich **nicht herunterladen lassen**: der FortiProxy antwortet auf
`https://api.nuget.org/v3-flatcontainer/*.nupkg` mit **HTTP 403** (Fortinet-
Blockseite), während die Metadaten-Endpunkte 200 liefern. px authentifiziert
dabei sauber — das ist kein 407 und kein px-Problem, sondern eine
Inhaltsfilterung auf den Paketdateien.

Folge: **die Versionen in `Directory.Packages.props` entsprechen dem
Offline-Feed `C:\NuGet-Local`, nicht dem Neuesten auf nuget.org.** Vor jedem
Bump prüfen, ob das Paket dort liegt:

```bash
ls C:/NuGet-Local | grep -i <paket>
```

Sonst ist der Restore auf dem Arbeitsplatz kaputt, während die GitHub Actions
grün bleiben (der Runner kommt an nuget.org heran).

## Aktueller Stand

**v0.1.0, 2026-08-25** — vollständig funktionsfähig, 196 Tests grün, gegen die
Produktivdomäne `LHP.INTERN` (DC03) verifiziert.

Was gebaut ist:

- **Suche** über sAMAccountName, displayName, cn, givenName, sn, mail, UPN als
  Teilstring; `employeeID` exakt. Gepaged, clientseitig sortiert, Limit aus den
  Einstellungen. Ein einzelner Treffer öffnet sich selbst.
- **Detailansicht** mit ~35 Attributen in fünf Abschnitten, Foto aus
  `thumbnailPhoto`, Statusabzeichen und Klartext der `userAccountControl`-Flags.
- **Gruppenliste** mit Herkunftskennzeichnung (Primärgruppe / direkt /
  verschachtelt), Volltextfilter, Schalter „nur Sicherheitsgruppen" und
  „verschachtelte auflösen".
- **Export**: Gruppen als CSV (Zwischenablage oder Datei, UTF-8 mit BOM),
  Konto als Textbericht.
- **Einstellungen** mit Verbindungstest, DPAPI-geschütztem optionalem Bind-Konto.
- **Tray** (Minimieren → Tray), **Single-Instance-Guard**, **Über-Fenster** mit
  echtem Self-Update gegen den Ordner-Kanal.
- **Optionale Gruppenbeschränkung beim Start** (`ldapeek.policy.json` neben der
  EXE, fehlt = keine Einschränkung).

Live gemessen am 2026-08-25 (eigenes Konto, `LHP.INTERN`):
Bind 41 ms · Suche 1,6 s · Detailabfrage 8 ms · 33 direkte Gruppen ·
132 effektive (99 geerbt) in 4,0 s · Primärgruppe „Domänen-Benutzer" korrekt
aus `primaryGroupID` hergeleitet.

## Roadmap

Nichts davon ist zugesagt — Kandidaten, die beim Bauen aufgefallen sind:

- **Rückwärtssuche: Mitglieder einer Gruppe.** Die naheliegende Ergänzung
  („wer ist alles in X?"). Braucht dieselbe Range-Retrieval-Behandlung auf
  `member` statt `memberOf` — die Bausteine liegen schon in `RangeRetrieval`.
- **Verschachtelungspfad anzeigen.** Aktuell steht bei einer geerbten Gruppe
  nur „verschachtelt", nicht *worüber*. `LDAP_MATCHING_RULE_IN_CHAIN` liefert
  den Pfad nicht mit; das hieße, den Baum clientseitig nachzubauen (pro Gruppe
  eine Abfrage auf `memberOf`, mit Zyklenschutz). Erst bauen, wenn es jemand
  wirklich braucht.
- **Gruppen zweier Konten vergleichen.** „Warum kann A das und B nicht?" ist die
  zweithäufigste Frage nach der, die LDAPeek schon beantwortet.
- **Computerkonten und Dienstkonten** — derzeit filtert
  `objectCategory=person` sie bewusst weg.
- **Deaktivierte Konten in der Trefferliste** werden clientseitig gefiltert.
  Bei sehr vielen Treffern wäre `(!(userAccountControl:1.2.840.113556.1.4.803:=2))`
  im Filter effizienter.
- Der Update-Ordner wird beim Start geprüft, aber nicht periodisch.

## Referenz

### Architektur

```
LDAPeek/
  Models/       AdUser, AdGroup, UserSearchHit, AppSettings, Display, Enums
  Services/     DirectoryService (LDAP), LdapFilter, AdValue, SidUtil,
                RangeRetrieval, EntryReader, SettingsService, JsonFileStore,
                SecretProtection, UpdateService, UpdateChannel, UserReport
  ViewModels/   MainWindowViewModel (+ .Groups.cs), SettingsWindowViewModel
  Views/        ChromeWindow, MainWindow, SettingsWindow, AboutWindow,
                TrayController, SingleInstanceGuard, GlobalExceptionHandler
  Controls/     TitleBar
  Logging/      MaskingLayoutRenderer
```

Die Geschäftslogik liegt vollständig in `Services/`; die ViewModels
orchestrieren nur. Deshalb sind 196 Tests ohne laufende Domäne und ohne
Headless-Avalonia möglich.

### Warum System.DirectoryServices.Protocols und nicht AccountManagement

`PrincipalContext`/`DirectorySearcher` gehen über ADSI/COM, verstecken die
tatsächlich angeforderten Attribute und machen genau die zwei Dinge umständlich,
auf die es hier ankommt: Range-Retrieval und `LDAP_MATCHING_RULE_IN_CHAIN`.
Nicht „zurückvereinfachen".

### Die drei Fallen im AD-Zugriff

Alle drei scheitern **still** — leere Liste statt Fehlermeldung:

1. **Range-Retrieval** (`Services/RangeRetrieval.cs`). Ab ~1500 Werten liefert
   der DC `memberOf` unter dem Namen `memberOf;range=0-1499`. Wer nur auf
   `memberOf` prüft, bekommt null Gruppen. Fällt erst beim ersten Konto mit
   vielen Mitgliedschaften auf — also bei dem, wo jemand eine verlässliche
   Auskunft braucht. Abgesichert in `RangeRetrievalTests`.
2. **Primärgruppe** (`Services/SidUtil.cs`). Sie steht in **keinem**
   `member`/`memberOf`, sondern nur als RID in `primaryGroupID`. Ohne die
   SID-Arithmetik (Domänen-SID ableiten, RID anhängen, per `objectSid` suchen)
   fehlt bei jedem Konto „Domänen-Benutzer".
3. **`objectCategory=person`**. `objectClass=user` allein liefert auch
   Computerkonten, weil `computer` davon erbt.

Dazu: `groupType` trägt das Vorzeichenbit (`0x80000000` = Sicherheitsgruppe) —
Vergleiche laufen über `unchecked((uint)…)`. Und `accountExpires` /
`msDS-UserPasswordExpiryTimeComputed` kodieren „nie" als `0` **und** als
`long.MaxValue`; beide müssen abgefangen werden, sonst steht im Fenster
„läuft ab am 01.01.1601".

### UI-Fallen (Avalonia 12)

- **`--` in einem XAML-Kommentar** bricht den Build mit AVLN1001 und einem
  XML-Parser-Stacktrace, der nicht nach „Kommentar" aussieht. Real hier
  passiert (`<!-- ---------- Stammdaten ---------- -->`). Ein Test in
  `XamlConsistencyTests` fängt das jetzt ab.
- **`Watermark` heißt `PlaceholderText`** (AVLN5001), auch auf `AutoCompleteBox`.
- **`net10.0-windows` ohne Versionssuffix** setzt `TargetPlatformVersion` auf
  7.0. Ein `SupportedOSPlatformVersion` darüber gibt NETSDK1135.
- **`XamlConsistencyTests` ist die Absicherung gegen die stillen Renderfehler**:
  tote `Classes=`-Verweise und fehlende `DynamicResource`-Schlüssel erzeugen
  keinen Compile-Fehler. Der Test wurde mit einem absichtlich verbogenen
  Schlüssel gegengeprüft — er wird rot.
- **`xunit.v3` bringt kein implizites `using Xunit;`** mit; deshalb
  `LDAPeek.Tests/GlobalUsings.cs`.

### CI: keine VSTest-Flags an `dotnet test`

Der erste CI-Lauf war rot, **obwohl kein Test fehlschlug**. Ursache war
`--logger "trx;LogFileName=test-results.trx"` im Workflow: xunit.v3 läuft auf
der Microsoft.Testing.Platform, die das VSTest-Flag nicht kennt — sie druckt
ihre Optionshilfe und führt **null Tests** aus. Der Lauf endet mit
„no tests were run" und Exitcode 5, was wie ein kaputtes Testprojekt aussieht.
Dasselbe gilt für `--nologo`, `--report-trx` und den `--`-Separator; `-c`,
`--no-build` und ein Projekt-/Solution-Argument gehen normal.

`--report-trx` bräuchte zusätzlich das Paket
`Microsoft.Testing.Extensions.TrxReport` — das liegt nicht im Offline-Feed und
ist über nuget.org nicht ladbar (siehe 403-Hinweis oben). Deshalb erzeugt der
Workflow die Fehler-Annotations aus der **Konsolenausgabe** statt aus einer TRX.
Dafür steht `DOTNET_CLI_UI_LANGUAGE: en-US` im Job: der Parser sucht `failed …`,
bei deutscher Ausgabe hieße die Zeile `fehlerhaft …` und er fände nichts.

Der Parser wurde gegen einen absichtlich kaputt gemachten Test gegengeprüft — er
erzeugt eine `::error title=…::`-Annotation mit Testname und Assertion-Meldung.
Das ist hier nicht optional: das Job-Log liefert die GitHub-API nur mit
Admin-Rechten am Repo (403), und `gh` ist auf dem Arbeitslaptop nicht
installiert. Ohne Annotation steht man vor einer Wand.

### Zugangsprüfung (`AccessPolicy` / `AccessGate`)

Optional. **Die Gruppe wird beim Bauen einkompiliert** — MSBuild-Property
`RequiredGroup` → `AssemblyMetadataAttribute("LDAPeek.RequiredGroup")`, im
Release-Workflow aus der Repo-Variable `LDAPEEK_REQUIRED_GROUP`. Ohne gesetzte
Gruppe gibt es keine Einschränkung.

**Warum nicht als Datei daneben** (erste Fassung war so, Lars hat es zu Recht
zerlegt): Eine `ldapeek.policy.json` kann jeder löschen, der sie findet — damit
läge die Hürde *unter* der, die das Werkzeug ziehen soll. Auszuschließen sind ja
gerade die, die **keine** LDAP-Abfrage schreiben können; eine JSON-Datei zu
löschen können sie sehr wohl. Einkompiliert muss man die Assembly dekompilieren
und patchen, und wer das kann, ist per `Get-ADUser` schneller.

Die Datei existiert weiter, kann aber nur noch den **Hinweistext** liefern
(kosmetisch) und eine Gruppe nur dann setzen, wenn keine einkompiliert ist.
Abgesichert in `AccessPolicyTests`: Datei löschen und Datei mit anderer Gruppe
heben die einkompilierte Regel beide nicht auf.

**Grenze, die bleibt:** Das trägt nur, solange der Nutzer die EXE nicht ersetzen
kann. In einem benutzerbeschreibbaren Ordner gehört ihm auch das Binary — für
eine belastbare Regel gehört LDAPeek nach `C:\Program Files\…`.

Zwei Wege, in dieser Reihenfolge:

1. **Anmeldetoken** (`WindowsIdentity.Groups`) — synchron, ~5 ms, deckt
   verschachtelte Gruppen und die Primärgruppe ab, funktioniert ohne Netz. Läuft
   in `OnFrameworkInitializationCompleted`, damit der Normalfall den Start nicht
   verzögert.
2. **LDAP-Rückfrage**, nur bei einem Nein und automatisch beim Öffnen des
   Abweisungsfensters. Das Token ist seit der Anmeldung eingefroren; eine frische
   Gruppenaufnahme steht dort nicht drin. Der Filter fragt umgekehrt
   (`GroupHasMemberRecursive`: „hat Gruppe X das Konto als Mitglied?") — eine
   Abfrage mit höchstens einem Treffer, gemessen ~10 ms, statt alle Gruppen des
   Kontos zu holen.

**Ein Nein ist immer ein Nein:** unauflösbare Gruppe, defekte Regeldatei oder
nicht erreichbares Verzeichnis führen zur Sperre, nicht zum Durchlassen — sonst
ließe sich die Regel durch einen Tippfehler oder das Zerstören einer Datei
aushebeln. Abgesichert in `AccessPolicyTests`/`AccessGateTests`.

**Falle beim Fensterwechsel:** In `App` darf im Startpfad **kein** eigenes
`Show()` stehen — die Desktop-Lifetime zeigt das gesetzte `MainWindow` nach
`OnFrameworkInitializationCompleted` selbst. Mit zusätzlichem `Show()` feuert
`Opened` zweimal, und die Verzeichnis-Rückfrage lief doppelt (im Log an zwei
Prüfungen im Abstand von 8 s zu sehen). Wird das Hauptfenster dagegen **nach**
dem Abweisungsfenster gestartet, ist der Startvorgang vorbei und `Show()` ist
Pflicht — dafür der `show`-Parameter an `StartMainWindow`.

### Nicht anfassen ohne Grund

- **`WriteInstallerScript` in `UpdateService`**: die Batch-Zeilen dürfen keine
  führende Einrückung haben (ein eingerücktes `:label` ist für cmd kein gültiges
  Sprungziel), und `TerminateForUpdate()` **muss** nach einem erfolgreichen
  `DownloadAndApplyAsync` gerufen werden — sonst wartet das Skript ewig auf das
  Prozessende und die Anzeige bleibt hängen.
- **`LandedOnInteractiveChild` in `TitleBar.axaml.cs`**: nicht als „dank
  ElementRole überflüssig" wegrefactoren. Zwei getrennte Mechanismen.
- **`MaskingLayoutRenderer.Register` als `[ModuleInitializer]`**: ein Aufruf in
  `Program.Main` deckt den Testprozess nicht ab, und dort verschwänden alle
  Log-Texte.
- **`SettingsService.Load` quarantänisiert nur bei `JsonException`**, nicht bei
  IO-Fehlern. Bei einer gesperrten Datei ist der Inhalt intakt — ein
  `.broken`-Move würde gute Daten wegräumen.
