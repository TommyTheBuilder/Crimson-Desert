# Prüfbericht Version 0.3

8. September 2026

Die Gegenstandsdaten stammen aus der tatsächlichen lokalen Installation. Der Katalog enthält alle 6.813 Einträge, 6.741 deutsche Namen und 72 ausdrücklich als intern bezeichnete Namen. 35 Archivverzeichnisse, 2.120.243 logische Dateieinträge, 853 relevante Tabellen/Sprachdateien und 1.600 Gegenstandsgruppen wurden verarbeitet. Alle 91 Katalogseiten sind erreichbar.

## Anwendungstests

- Windows-x64-Oberfläche kompiliert; 18 WPF-Prüfungen einschließlich Bereichsfilter, automatischem Laden, Auswahl nach Aktualisierung und Entfernen veralteter Listen bestanden.
- Katalogtests: vollständige Seiten, Unicode-Suche, exakte Schlüssel, Filter, große Stapelgrenzen und beschädigte Archiv-/Sprachdaten geprüft.
- Hintergrundprozess: Eingabevalidierung, gesperrte Schreibbefehle, UTF-8-Transport, binär identische Sicherung mit Hashmanifest und reguläres Beenden geprüft.
- Externer Lesertransport: eigener versteckter Prozess, richtige Antwortzuordnung, ausschließlich erlaubte Lesebefehle, Zeitlimits, unerwartetes Prozessende und Schließen per EOF geprüft.
- Die neuen Inventarstrukturen wurden zusätzlich an getrennten Speicher-Fixtures geprüft. Native Schreibabläufe aus früheren Simulationen sind kein Nachweis für funktionierende Cheats und werden von dieser Fassung nicht aufgerufen.

## Spieltest und Stabilität

Der Rohdatenabgleich im laufenden Spiel konnte das echte Inventar lesen. Die anschließend verwendete Frida-Verbindung verursachte einen protokollierten Spielabsturz. Die neue Fassung verwendet daher einen separaten Windows-Leser; Details und aktuelle Prüfergebnisse stehen in LIVE-PRUEFUNG.md.

Es wurden keine Spieldateien, Spielstände, Gegenstandsmengen oder Spielerwerte verändert. Die aktuelle Fassung ist ein Katalog- und Inventarleser; die gewünschte umfangreiche Cheat-Funktionalität ist noch nicht fertig.

## Reparatur der Auslieferung am 8. September 2026

Die Meldung „Inventarleser konnte nicht gestartet werden: spawn … PywelReader.exe ENOENT“ entstand, weil runtime/PywelReader.exe im Programmordner und im aktuellen ZIP fehlte. Auch source/reader war nicht mitgeliefert. Der Leser wurde aus dem vorhandenen Quelltext neu erstellt und zusammen mit seinen Quelldateien ergänzt.

Der tatsächliche ausgelieferte Leser startet nun über die mitgelieferte Node.js-Laufzeit und beantwortet JSON-Anfragen korrekt. Geprüft wurden Status ohne Spielverbindung, die Ablehnung einer unvollständigen Verbindungsanfrage und reguläres Beenden. Ein fehlender Leser führt jetzt zu einer verständlichen Meldung mit Entpackhinweis und erwartetem Dateipfad; dieser Fehlerfall wurde ebenfalls geprüft.

Nach dem Spielstart durch den Benutzer wurde der ausgelieferte Hintergrundprozess mit Crimson Desert verbunden. Nach dem Laden des Spielstands gelangen zwei aufeinanderfolgende Inventarabfragen: jeweils 117 belegte Plätze, davon 68 Figureninventar, 9 Geld/Marken, 21 Quest, 18 Lager und 1 interner Eintrag. Alle 117 Einträge erhielten Namen aus dem lokalen Katalog. Die abschließende Statusabfrage erkannte weiterhin die geladene Spielfigur. Die Testverbindung wurde anschließend regulär getrennt.

Die Paketerstellung ergänzt den Leser künftig automatisch und prüft die Pflichtdateien vor der Veröffentlichung. Beide ZIP-Dateien wurden erneuert; auch der Leserstart aus einer frisch entpackten ZIP mit deren eigener Node.js-Laufzeit war erfolgreich. Diese Prüfungen belegen die reparierte Auslieferung und die kurzen Lesevorgänge, keine Langzeitstabilität.
