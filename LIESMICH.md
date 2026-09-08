# Pywel Trainer für Crimson Desert

Version 0.5 – Gegenstandskatalog, Inventarleser, **Live-Item-Spawner** und sicherer Offline-Save-Editor für Steam-Build 25116796 / EXE-Dateiversion 1.0.0.2760.

**Verfügbar:** vollständiger Gegenstandskatalog aus der Installation, Inventar mit Mengen und deutschen Namen, getrennte Inventarbereiche, Anzeige der Spielerwerte, **Gegenstände während des Spielens spawnen**, Offline-Hinzufügen mit automatischer Sicherung, Spielstandsicherungen und Diagnoseexport.

**Noch nicht verfügbar:** direktes Geldsetzen, Mengen vorhandener Laufzeit-Slots ändern, Spielerwerte auffüllen und Teleportieren.

> Nur für Singleplayer verwenden. Nicht in Online-/Anti-Cheat-Modi einsetzen.

## Spiel automatisch finden

Der Trainer verwendet keinen fest verdrahteten `F:\...`-Pfad mehr. Er sucht `CrimsonDesert.exe` in dieser Reihenfolge:

1. bereits gespeicherter gültiger Installationspfad,
2. Pfad des aktuell laufenden `CrimsonDesert.exe`-Prozesses,
3. Steam-Installationsordner plus alle Einträge aus `steamapps\libraryfolders.vdf`,
4. Epic-Launcher-Manifeste unter `%PROGRAMDATA%`.

Wird das Spiel trotzdem nicht gefunden, Crimson Desert einmal starten und anschließend erneut „Mit Spiel verbinden“ drücken. Trainer und Spiel müssen auf derselben Windows-Rechteebene laufen.

## Live Gegenstände spawnen

1. Crimson Desert starten und den gewünschten Spielstand vollständig laden.
2. Pywel Trainer starten bzw. „Mit Spiel verbinden“ drücken.
3. Unter „Geld & Waffen“ einen Gegenstand im lokalen Katalog auswählen.
4. Menge eingeben und „Zum Inventar hinzufügen“ drücken.
5. Die mitgelieferte `runtime\PywelInjector.exe` lädt `runtime\PywelLive.dll` in den **bereits laufenden** Crimson-Desert-Prozess. Danach kommuniziert der Trainer ausschließlich lokal über die Named Pipe `PywelTrainer-CrimsonDesert`.
6. Der Live-Helfer löst den internen Gegenstandsschlüssel gegen die Gegenstandstabelle der laufenden Spielversion auf und nutzt den Item-Erzeugungs-/Inventar-Transaktionspfad der Engine. Der Gegenstand erscheint ohne Neustart im laufenden Inventar.

Der Live-Spawner schreibt nicht einfach rohe Item-Strukturen in freie Slots. Das ist absichtlich so: ein Item benötigt eine gültige Instanz-ID und muss sowohl in die Client- als auch in die serverseitige Inventarspiegelung gelangen. Der zugrunde liegende Pfad basiert auf dem MIT-Projekt `XeTrinityz/Trinity` und wird beim Build aus einem fest gepinnten Commit erstellt.

Wenn die Live-Signaturen nach einem Spielupdate nicht mehr passen, meldet der Trainer „Live-Spawner noch nicht bereit“ bzw. eine konkrete Fehlermeldung und führt **keinen** unsicheren Fallback-Rohwrite aus.

## Offline-Hinzufügen als Fallback

Wenn Crimson Desert geschlossen ist, bleibt der bisherige sichere Save-Editor verfügbar:

1. Spiel speichern und vollständig schließen.
2. Gegenstand und Menge auswählen.
3. „Zum Inventar hinzufügen“ klicken.
4. Der neueste gefundene `save.save` wird vor der Änderung vollständig gesichert.
5. Der Save-Editor erzeugt zunächst eine temporäre Datei, validiert diese und ersetzt erst danach den Original-Save. Bei einem Fehler wird zurückgerollt.

Steam- und Epic-Spielstände unter `%LOCALAPPDATA%\Pearl Abyss\CD...` werden berücksichtigt.

## Inventar lesen

`runtime\PywelReader.exe` bleibt ein separater **read-only** Prozess. Er öffnet Crimson Desert nur mit Windows-Leserechten und liest Spieler-/Inventarwerte. Das Live-Spawning ist davon getrennt und läuft ausschließlich über `PywelLive.dll`.

Die externe Leseerkennung prüft Dateiversion, Hauptmodulgröße, PE-Identität und eindeutige Laufzeit-Signaturen. Wenn eine Struktur nicht eindeutig ist, wird der Zugriff verweigert statt geraten.

## Vollständiger Gegenstandskatalog

Der Katalog wird lokal aus den installierten PAZ-Spielarchiven, `ItemInfo`, `ItemGroupInfo` und den Sprachdateien erstellt. Es wird keine fertige heruntergeladene Gegenstandsliste verwendet. Namen, Beschreibungen, interner Schlüssel, permanente Spieldaten-ID, Stapelgrenze und Gruppen kommen aus deiner Installation.

Die Suche erfasst deutsche/englische Namen, Beschreibungen, interne Schlüssel und IDs. Interne oder unbenutzte Definitionen können im Katalog sichtbar sein; die Live-Engine kann solche Einträge beim Hinzufügen ablehnen.

## Sicherungen und Diagnose

„Spielstände sichern“ legt vollständige Kopien unter `data\backups` ab. Offline-Hinzufügen erstellt zusätzlich automatisch eine Sicherung vor jeder Änderung.

„Protokoll → Diagnose speichern“ schreibt einen Bericht nach `data\diagnostics`. Version 0.5 enthält darin zusätzlich:

- automatisch erkannten Spielpfad,
- laufende Prozess-/Buildinformationen,
- Status von `PywelInjector.exe` und `PywelLive.dll`,
- Named-Pipe-/Live-Spawner-Status,
- Save-Editor-Verfügbarkeit,
- letzte Fehlermeldungen.

## Release bauen

`source\build.ps1` erzeugt bzw. prüft:

- `PywelTrainer.exe`
- `runtime\PywelReader.exe`
- `runtime\PywelSaveEditor.exe`
- `runtime\PywelInjector.exe`
- `runtime\PywelLive.dll`
- vorhandene `runtime\node.exe`

Der Live-Build lädt den gepinnten Trinity-Quellstand, fügt nur die Pywel-Named-Pipe-Brücke hinzu und baut eine headless Variante ohne Ingame-Menü/DX12-Overlay. GitHub Actions prüft alle Node-Quellen, kompiliert Windows-x64 und erzeugt eine portable ZIP.

## Dateien und Quellen

- `source`: WPF-Trainer und Release-Buildskript.
- `source/reader`: externer read-only Inventarleser.
- `source/save-editor`: reproduzierbarer Build des Offline-Save-Editors.
- `source/live`: Buildskript, x64-Injektor und lokale Named-Pipe-Brücke für Live-Spawning.
- `app/live-client.js`: lokale Kommunikation zwischen Trainer und Live-DLL.
- `runtime`: Node.js und die gebauten Windows-Komponenten.
- `licenses`: Lizenz-/Quellhinweise für Trinity, Save-Editor und weitere Komponenten.

Pywel Trainer ist ein unabhängiges Community-Werkzeug und kein offizielles Pearl-Abyss-Programm.