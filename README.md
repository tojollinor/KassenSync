# KassenSync

KassenSync ist eine Windows-Anwendung aus **WPF-GUI + Windows-Dienst**. Der Dienst überwacht einen konfigurierbaren Quellordner, indexiert neue Dateien anhand **Dateiname + SHA-256** und kopiert sie auf ein konfiguriertes Ziellaufwerk.

Bereits erfolgreich ausgegebene Dateien werden nicht automatisch erneut kopiert, auch wenn sie später vom Zielmedium gelöscht werden. Über die GUI können einzelne oder mehrere markierte Dateien bewusst erneut ausgegeben werden.

## Aktueller Entwicklungsstand

- Windows-Dienst und WPF-GUI
- SQLite-Index unter `%ProgramData%\\KassenSync`
- konfigurierbarer Quellordner und optionale Unterordner
- konfigurierbare Dateiendungen
- `FileSystemWatcher` plus Sicherheits-Rescan
- Stabilitätsprüfung vor Hashing/Kopieren
- SHA-256-Indexierung über Dateiname + Hash
- festes, schnell änderbares Ziellaufwerk, z. B. `E:\\`
- frei wählbarer Ziel-Unterordner
- Übernahme der relativen Quellordnerstruktur
- Wartestatus bei fehlendem Zielmedium
- sicheres Kopieren über temporäre `.part`-Datei
- WPF-Dateiübersicht mit Mehrfachauswahl, Status, Zeitstempeln und Ausgabezähler
- manuelle Wiederausgabe markierter Dateien
- Einstellungsseite für Quelle, Unterordner, Ziel, Dateitypen und Updateprüfung
- konfigurierbarer GUI-Autostart per aktuellem Windows-Benutzer
- Named-Pipe-Kommunikation mit ACL zwischen normalem Benutzer und LocalSystem-Dienst

## Noch geplant

- Topmost-Kopierfortschritt und 3-Sekunden-Erfolg-/Fehlermeldung
- Auto-Update direkt über GitHub Releases
- Installer und GitHub-Actions-Releasepipeline

## Architektur

- `KassenSync.Core`: Modelle, Datenbank, Einstellungen, Hashing und Kopierfunktionen
- `KassenSync.Service`: Windows-Dienst, Watcher, Indexierung, Kopierwarteschlange und IPC-Server
- `KassenSync.App`: WPF-GUI und IPC-Client

Der Windows-Dienst zeigt selbst keine Fenster. Benutzerinteraktion findet ausschließlich in der WPF-GUI statt.
