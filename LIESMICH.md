# Pywel Trainer für Crimson Desert

Version 0.3 – externer Gegenstandskatalog und Inventarleser für Steam-Build 25116796 / EXE 1.0.0.2760.

**Verfügbar:** vollständiger Gegenstandskatalog aus der Installation, Inventar mit Mengen und deutschen Namen, getrennte Inventarbereiche, Anzeige der Spielerwerte, Spielstandsicherungen und Diagnoseexport.

**Noch nicht verfügbar:** Gegenstände hinzufügen, Geld oder Mengen ändern, Spielerwerte auffüllen und Teleportieren. Diese Funktionen sind in der Oberfläche gesperrt. Dies ist noch kein vollständig funktionierender Cheat-Trainer.

## Starten

1. Den ganzen Ordner zusammenlassen und PywelTrainer.exe starten.
2. Crimson Desert starten, einen Spielstand laden und im Trainer „Mit Spiel verbinden“ wählen.
3. „Geld & Waffen“ öffnen. Das Figureninventar wird nach der Verbindung automatisch geladen.
4. Über den Bereichsfilter Figureninventar, Geld & Marken, Questgegenstände, Lager oder alle Bereiche auswählen.
5. „Inventar aus dem Spiel laden“ aktualisiert die Momentaufnahme nach Änderungen im Spiel. Das Lesen benötigt keine Bewegung und keinen Kauf.

Voreingestellter Spielordner: F:\Steam\steamapps\common\Crimson Desert.

## Was geändert wurde

Die alte Verbindungskomponente verursachte bei der Prüfung einen Spielabsturz. Sie wird von dieser Fassung nicht mehr verwendet. PywelReader.exe läuft als separater Prozess und liest mit Windows-Leserechten. Es werden keine DLL, keine Spiel-Callbacks und keine Fernaufrufe in Crimson Desert installiert. Der Inventarleser besitzt keine Schreib-, Thread-Erstellungs- oder Beendigungsrechte am Spiel.

Die neue Erkennung berücksichtigt die veränderten Datentabellen und Inventarplätze von EXE 1.0.0.2760. Unvollständige oder widersprüchliche Momentaufnahmen werden abgewiesen. Andere Spielversionen benötigen ein geprüftes Leseprofil; der Offline-Katalog bleibt dabei unabhängig verfügbar.

## Vollständiger Gegenstandskatalog

Alle 6.813 Datensätze der installierten Gegenstandstabelle werden aus den lokalen PAZ-Spielarchiven gelesen. 6.741 Namen sind deutsch; 72 Einträge zeigen ihren internen Namen, weil kein deutscher oder englischer Name vorhanden ist. Enthalten sind vorhandene Beschreibungen, permanente Spieldaten-ID, interner Schlüssel, maximale Stapelgröße und Gegenstandsgruppen.

Der Katalog enthält 532 Waffen, 829 Rüstungs-/Zubehörgegenstände, 288 Materialien, 338 Verbrauchsgegenstände, 39 Geld-/Währungsgegenstände, 514 Questgegenstände und 4.273 sonstige Einträge. Auch interne oder unbenutzte Gegenstände sind enthalten; Sichtbarkeit bedeutet nicht, dass sie regulär erhältlich oder hinzufügbar sind.

Die Suche erfasst Namen, Beschreibungen, interne Schlüssel und IDs. Alle 91 Seiten sind über Zurück/Weiter erreichbar. „Neu einlesen“ erstellt den Katalog erneut aus der Installation. Es wird keine heruntergeladene Gegenstandsliste verwendet.

## Inventar und Spielerwerte

Inventarlisten sind Momentaufnahmen. Die Mengen stammen aus dem laufenden Spiel. Namen werden über den exakten internen Schlüssel und die permanente ID aus dem lokalen Katalog ergänzt. Eine permanente Spieldaten-ID wird nicht als Laufzeit-ID oder Speicheradresse verwendet.

Sehr große Stapelgrenzen werden als exakte Dezimalzeichenfolge erhalten. Geld & Marken enthält auch Lager- und Beitragsressourcen des Spiels; ein direktes Setzen des ausgebbaren Geldbetrags ist nicht enthalten. Spielerwerte werden als prozentualer Füllstand angezeigt; das Auffüllen ist gesperrt.

Die getrennten Speicherbereiche hängen vom Spielstand ab. Die Option „Alle Bereiche“ kann interne, nicht regulär im Rucksack sichtbare Gegenstände enthalten. Bei einem Figurenwechsel oder Laden eines anderen Spielstands werden veraltete Listen entfernt.

## Sicherungen und Fehler

„Spielstände sichern“ kopiert die gefundenen Spielstände nach data\backups. Jede vollständige Sicherung enthält ein Hashmanifest und einen save-Unterordner. UNVOLLSTAENDIG.txt kennzeichnet einen abgebrochenen Versuch. Die Sicherung überschreibt keine Spielstände.

Unter „Protokoll → Diagnose speichern“ entsteht ein Bericht in data\diagnostics. Bei fehlender Erkennung nennt der Trainer den Grund. Wiederholtes Klicken schaltet nicht unterstützte Cheats nicht frei.

Zum Wiederherstellen das Spiel schließen, den aktuellen Spielstandordner separat behalten und eine vollständige Sicherung an den im Manifest genannten Ursprungsort kopieren. Bei Steam-Cloud-Konflikten bewusst die gewünschte Kopie wählen.

## Dateien und Quellen

- source: C#-Oberfläche und Buildskript.
- source/reader: Quelltext und Buildskript des externen Windows-Lesers.
- app: Hintergrundprozess, Archivleser und Katalogsuche.
- runtime: mitgelieferte Node.js-Laufzeit und PywelReader.exe.
- licenses: Quellen- und Lizenzhinweise.
- data: lokale Einstellungen, Protokolle, Cache, Sicherungen und Diagnosen; nicht Teil der ZIP-Datei.

Spielstrukturen wurden anhand der installierten EXE geprüft. Frühere Grundlagen stammen aus dem MIT-Projekt XeTrinityz/Trinity; weitere Primärquellen und Archivformat-Lizenzen stehen in licenses. Dies ist ein unabhängiges Programm und kein offizielles Pearl-Abyss-Werkzeug.
