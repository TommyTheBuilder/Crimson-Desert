# Prüfung am laufenden Spiel

8. September 2026 · Trainer 0.3 · Steam-Build 25116796 · Dateiversion 1.0.0.2760

## Befund und behobene Ursache

Die vorherigen Spielerkennungen und Speicherabstände passten nicht zur installierten Spielversion. Die neuen Erkennungen und Lesestrukturen wurden anhand der tatsächlichen EXE und im laufenden Spiel geprüft. Der erste erfolgreiche Rohdatenabgleich ergab 117 belegte Plätze: 68 Figureninventar, 9 Geld/Marken, 21 Quest, 18 Lager und 1 interner Gegenstand. Das Inventarmenü des Spiels zeigte ebenfalls 68 Figureninventarplätze. Die separat gelesene Clientkopie bestätigte diese Bereiche.

Anschließend stürzte der Spielprozess während des Tests ab. Windows protokollierte am 08.09.2026 um 14:07 Uhr einen Fehler in frida-agent.dll (0xc0000409). Diese Verbindungskomponente wird im ausgelieferten Hintergrundprozess deshalb vollständig durch einen externen Windows-Leser ersetzt. Der Absturz wird nicht als erfolgreicher Stabilitätstest dargestellt.

## Neue Verbindung

PywelReader.exe läuft außerhalb des Spiels. Es verwendet ausschließlich Prozessabfragen und ReadProcessMemory; keine DLL-Injektion, keine Spiel-Callbacks und keine nativen Inventar-Schreibaufrufe. Ein Fehler oder Zeitlimit beendet nur den eigenen Leserprozess. Beim Schließen des Trainers werden dessen eigene Hintergrundprozesse beendet.

Die aktuelle Struktur verwendet Datensatzzeiger +0x58, Inventarplätze mit 0xC8 Byte und den Standard-Speicherbereich bei +0x428. Die Spielerkennung wird durch unabhängige Signaturen geprüft. Inventarbereiche werden in begrenzte Puffer gelesen; widersprüchliche Momentaufnahmen und veraltete Rückverweise werden abgewiesen.

## Einschränkung

Gegenstände hinzufügen, Mengenänderungen, Auffüllen und Reisen sind deaktiviert. Beim Hinzufügen wurden aktuelle Funktionen und deren geänderte Aufrufkonventionen statisch gefunden. Vollständige Synchronisierung, Veröffentlichung und Übernahme nach Speichern/Neuladen sind jedoch nicht validiert. Es wurde kein Gegenstand hinzugefügt und keine Menge oder kein Spielerwert verändert.

Die früheren Diagnoseberichte bleiben als Verlauf in data/diagnostics erhalten und beschreiben den jeweiligen damaligen Stand. Aktuelle externe Prüfergebnisse werden im ergänzten Abschnitt dieses Berichts festgehalten.

## Erneute Prüfung der reparierten Auslieferung

Am 8. September 2026 um 14:45 Uhr wurde die zuvor fehlende runtime/PywelReader.exe über den ausgelieferten Hintergrundprozess gestartet. Der Spielstart und das Laden des Spielstands wurden durch den Benutzer vorgenommen. Die externe Verbindung erkannte zunächst noch keine geladene Spielfigur und anschließend den geladenen Spielstand korrekt.

Zwei Inventarabfragen lieferten jeweils 117 belegte Plätze: 68 Figureninventar, 9 Geld/Marken, 21 Quest, 18 Lager und 1 interner Eintrag. Alle Einträge wurden mit Namen aus dem lokalen Katalog ergänzt. Die abschließende Statusabfrage war erfolgreich; danach wurde ausschließlich die eigene Testverbindung getrennt. Es erfolgten keine Schreibzugriffe auf das Spiel. Dies war ein kurzer Funktionstest, kein Langzeittest.
