# OrdnerSync

OrdnerSync ist eine Windows-Anwendung aus **WPF-GUI + Windows-Dienst** für zuverlässige Dateiübertragung und Synchronisation zwischen Ordnern und USB-Zielen.

## Funktionen

- mehrere unabhängige Sync-Jobs
- Jobs aktiv/inaktiv schaltbar
- Einweg-Synchronisation `A → B`
- bidirektionale Synchronisation `A ↔ B`
- Konflikterkennung statt blindem Überschreiben
- normale Ordner und USB-Sticks als Ziel
- persistenter Wartezustand bei fehlendem USB-Laufwerk
- Nachfrage beim späteren Wiederverbinden eines wartenden USB-Ziels
- keine automatische Löschweitergabe
- `FileSystemWatcher` plus Sicherheits-Rescan
- Stabilitätsprüfung vor Hashing und Kopieren
- Deduplizierung über Job, Herkunftsseite, Dateiname und SHA-256
- SHA-256-Prüfung der temporär kopierten Datei vor dem finalen Ersetzen
- Erhalt der relativen Ordnerstruktur
- SQLite-Index unter `%ProgramData%\OrdnerSync`
- Migration bestehender 0.2.x-Einstellungen und Datei-Historie
- Checkbox-Auswahl mit zentraler Kopf-Checkbox
- markierte Indexeinträge können entfernt werden, ohne Quelldateien zu löschen
- manuelle erneute Verarbeitung markierter Dateien
- Dateiübersicht nach Job filterbar
- farbige Statuspunkte für Jobs, Dateien und Dienst
- Windows-Dienststatus sowie Start/Stop/Neustart in den Einstellungen
- Infotray-Symbol, Fenster läuft beim Schließen/Minimieren im Tray weiter
- konfigurierbarer GUI-Autostart
- Kopierfortschritt als Topmost-Fenster
- Fehler immer persistent mit Bestätigung
- Erfolgsmeldungen: Aus, zeitgesteuert oder persistent mit Bestätigung
- einstellbare Dauer zeitgesteuerter Erfolgsmeldungen
- hochauflösende Logo-Darstellung aus der größten verfügbaren Icon-Ebene
- automatische oder manuelle Updateprüfung über GitHub Releases
- self-contained Windows-x64-Installer

## Sicherheit der Synchronisation

Bei bidirektionalen Jobs speichert OrdnerSync pro Datei den letzten gemeinsamen SHA-256-Stand. Wurde dieselbe Datei seitdem auf beiden Seiten unterschiedlich verändert, wird **keine Seite automatisch überschrieben**. Der Eintrag erhält stattdessen den Status **Konflikt**.

Löschungen werden bewusst nicht auf die Gegenseite übertragen. Wird eine bereits synchronisierte Datei auf einer Seite gelöscht, stellt OrdnerSync sie ebenfalls nicht ungefragt wieder her.

## Upgrade

Bestehende Einstellungen aus OrdnerSync 0.2.x werden automatisch als **Job 1** übernommen. Da 0.2.x auf die Ausgabe an einen Wechseldatenträger ausgelegt war, wird dieser migrierte Job als USB-Ziel behandelt.

Die bestehenden internen .NET-Namespaces heißen aus Kompatibilitätsgründen vorerst weiterhin `KassenSync.*`. Sichtbare Produktnamen, Assemblies, Dienst, Datenpfade und Installer heißen **OrdnerSync**.
