# Pywel Trainer für Crimson Desert

Version 0.4 – externer Gegenstandskatalog, Inventarleser und sicheres Hinzufügen von Gegenständen für Steam-Build 25116796 / EXE 1.0.0.2760.

**Verfügbar:** vollständiger Gegenstandskatalog aus der Installation, Inventar mit Mengen und deutschen Namen, getrennte Inventarbereiche, Anzeige der Spielerwerte, **Gegenstände hinzufügen**, Spielstandsicherungen und Diagnoseexport.

**Noch nicht verfügbar:** direktes Geldsetzen, Mengen vorhandener Laufzeit-Slots ändern, Spielerwerte auffüllen und Teleportieren. Diese Funktionen bleiben in der Oberfläche gesperrt.

## Starten und Inventar lesen

1. Den ganzen Ordner zusammenlassen und PywelTrainer.exe starten.
2. Crimson Desert starten, einen Spielstand laden und im Trainer „Mit Spiel verbinden“ wählen.
3. „Geld & Waffen“ öffnen. Das Figureninventar wird nach der Verbindung automatisch geladen.
4. Über den Bereichsfilter Figureninventar, Geld & Marken, Questgegenstände, Lager oder alle Bereiche auswählen.
5. „Inventar aus dem Spiel laden“ aktualisiert die Momentaufnahme nach Änderungen im Spiel. Das Lesen benötigt keine Bewegung und keinen Kauf.

Voreingestellter Spielordner: F:\Steam\steamapps\common\Crimson Desert.

## Gegenstände hinzufügen

Das Hinzufügen verwendet absichtlich **keine DLL-Injektion und keine direkten Speicher-Schreibzugriffe in Crimson Desert**. Stattdessen wird der geschlossene Spielstand bearbeitet und vor jeder Änderung automatisch gesichert.

1. Im Spiel den gewünschten Spielstand speichern.
2. **Crimson Desert vollständig schließen.** Solange ein `CrimsonDesert`-Prozess läuft, verweigert der Trainer jede Spielstandänderung.
3. Pywel Trainer geöffnet lassen bzw. starten und „Geld & Waffen“ öffnen.
4. Einen Gegenstand im lokalen Katalog auswählen und die gewünschte Anzahl eingeben.
5. „Zum Inventar hinzufügen“ anklicken.
6. Der Trainer nimmt den zuletzt geänderten gefundenen `save.save` als Ziel, erstellt zuerst eine datierte Sicherung unter `data\backups`, erzeugt den geänderten Spielstand in einer temporären Datei, validiert ihn und ersetzt erst danach atomar den Original-Spielstand.
7. Crimson Desert wieder starten und den betreffenden Spielstand laden.

Der Ziel-Slot wird unter dem Gegenstand angezeigt. Wenn mehrere Steam-/Epic-Spielstände existieren, wird der zuletzt geänderte Slot verwendet. Eine freie Slot-Auswahl ist für eine spätere UI-Erweiterung vorgesehen.

Die Gegenstands-ID wird nicht frei eingegeben: Der Backend-Prozess akzeptiert nur einen Gegenstand, dessen `itemKey` und interner Schlüssel exakt im aktuell aus der installierten Spielversion gelesenen Katalog vorkommen. Damit werden versehentliche Fremd- oder Fantasie-IDs blockiert.

## Was geändert wurde

Die alte Verbindungskomponente verursachte bei der Prüfung einen Spielabsturz. Sie wird von dieser Fassung nicht mehr verwendet. PywelReader.exe läuft als separater Prozess und liest mit Windows-Leserechten. Es werden keine DLL, keine Spiel-Callbacks und keine Fernaufrufe in Crimson Desert installiert. Der Inventarleser besitzt keine Schreib-, Thread-Erstellungs- oder Beendigungsrechte am Spiel.

Für „Gegenstand hinzufügen“ kommt separat `runtime\PywelSaveEditor.exe` zum Einsatz. Dieser Helfer wird beim Release-Build aus dem gepinnten MPL-2.0-Projekt `NattKh/CRIMSON-DESERT-SAVE-EDITOR-AND-GAME-MODS` gebaut. Er bearbeitet ausschließlich die Save-Datei, wenn das Spiel geschlossen ist. Quelle, Commit und Lizenz stehen unter `licenses\NOTICE.md`; die MPL-2.0-Lizenz wird beim Build nach `licenses\CrimsonSaveEditor-MPL-2.0.txt` kopiert.

Die neue Erkennung berücksichtigt die veränderten Datentabellen und Inventarplätze von EXE 1.0.0.2760. Unvollständige oder widersprüchliche Momentaufnahmen werden abgewiesen. Andere Spielversionen benötigen ein geprüftes Leseprofil; der Offline-Katalog und die Spielstandsuche bleiben dabei unabhängig verfügbar.

## Vollständiger Gegenstandskatalog

Alle 6.813 Datensätze der installierten Gegenstandstabelle werden aus den lokalen PAZ-Spielarchiven gelesen. 6.741 Namen sind deutsch; 72 Einträge zeigen ihren internen Namen, weil kein deutscher oder englischer Name vorhanden ist. Enthalten sind vorhandene Beschreibungen, permanente Spieldaten-ID, interner Schlüssel, maximale Stapelgröße und Gegenstandsgruppen.

Der Katalog enthält 532 Waffen, 829 Rüstungs-/Zubehörgegenstände, 288 Materialien, 338 Verbrauchsgegenstände, 39 Geld-/Währungsgegenstände, 514 Questgegenstände und 4.273 sonstige Einträge. Auch interne oder unbenutzte Gegenstände sind enthalten; Sichtbarkeit bedeutet nicht, dass sie regulär im Spiel erhältlich sind.

Die Suche erfasst Namen, Beschreibungen, interne Schlüssel und IDs. Alle 91 Seiten sind über Zurück/Weiter erreichbar. „Neu einlesen“ erstellt den Katalog erneut aus der Installation. Es wird keine heruntergeladene Gegenstandsliste verwendet.

## Inventar und Spielerwerte

Inventarlisten sind Momentaufnahmen. Die Mengen stammen aus dem laufenden Spiel. Namen werden über den exakten internen Schlüssel und die permanente ID aus dem lokalen Katalog ergänzt. Eine permanente Spieldaten-ID wird nicht als Laufzeit-ID oder Speicheradresse verwendet.

Sehr große Stapelgrenzen werden als exakte Dezimalzeichenfolge erhalten. Geld & Marken enthält auch Lager- und Beitragsressourcen des Spiels; ein direktes Setzen des ausgebbaren Geldbetrags ist nicht enthalten. Spielerwerte werden als prozentualer Füllstand angezeigt; das Auffüllen ist gesperrt.

Die getrennten Speicherbereiche hängen vom Spielstand ab. Die Option „Alle Bereiche“ kann interne, nicht regulär im Rucksack sichtbare Gegenstände enthalten. Bei einem Figurenwechsel oder Laden eines anderen Spielstands werden veraltete Listen entfernt.

## Sicherungen und Fehler

„Spielstände sichern“ kopiert die gefundenen Spielstände nach `data\backups`. Beim Hinzufügen wird zusätzlich automatisch der komplette ausgewählte Slot gesichert, **bevor** der Save-Editor gestartet wird. Jede vollständige Sicherung enthält ein Hashmanifest und einen `save`-Unterordner. `UNVOLLSTAENDIG.txt` kennzeichnet einen abgebrochenen Versuch.

Unter „Protokoll → Diagnose speichern“ entsteht ein Bericht in `data\diagnostics`. Der Diagnosebericht enthält ab Version 0.4 auch Verfügbarkeit und Pfad des Save-Editor-Helfers sowie den gefundenen Ziel-Slot.

Zum Wiederherstellen Crimson Desert schließen, den aktuellen Spielstandordner separat behalten und eine vollständige Sicherung an den im Manifest genannten Ursprungsort kopieren. Bei Steam-Cloud-Konflikten bewusst die gewünschte Kopie wählen.

## Release bauen

`source\build.ps1` erzeugt jetzt zwingend alle drei ausführbaren Komponenten:

- `PywelTrainer.exe`
- `runtime\PywelReader.exe`
- `runtime\PywelSaveEditor.exe`

Für `PywelSaveEditor.exe` werden Git, CMake und die Visual-Studio-C++-Buildtools benötigt. Das Buildskript lädt ausschließlich den gepinnten Upstream-Commit und baut den CLI-Helfer `parc_engine_cli` als Windows-x64-Programm. Fehlt eine der drei EXEs, schlägt der Release-Build fehl.

## Dateien und Quellen

- `source`: C#-Oberfläche und Release-Buildskript.
- `source/reader`: Quelltext und Buildskript des externen Windows-Lesers.
- `source/save-editor`: reproduzierbares Buildskript für den gepinnten Save-Editor-Helfer.
- `app`: Hintergrundprozess, Archivleser, Katalogsuche und sichere Save-Edit-Orchestrierung.
- `runtime`: Node.js-Laufzeit, `PywelReader.exe` und im vollständigen Build `PywelSaveEditor.exe`.
- `licenses`: Quellen- und Lizenzhinweise.
- `data`: lokale Einstellungen, Protokolle, Cache, Sicherungen und Diagnosen; nicht Teil der sauberen Quellverteilung.

Spielstrukturen wurden anhand der installierten EXE geprüft. Frühere Grundlagen stammen aus dem MIT-Projekt XeTrinityz/Trinity; weitere Primärquellen, die MPL-2.0-Quelle des Save-Editors und Archivformat-Lizenzen stehen in `licenses`. Dies ist ein unabhängiges Programm und kein offizielles Pearl-Abyss-Werkzeug.
