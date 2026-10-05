# KassenSync

KassenSync ist eine Windows-Anwendung aus WPF-GUI und Windows-Dienst. Der Dienst überwacht einen konfigurierbaren Quellordner, indexiert neue Dateien anhand **Dateiname + SHA-256** und kopiert sie auf ein konfiguriertes Ziellaufwerk.

Bereits erfolgreich ausgegebene Dateien werden nicht automatisch erneut kopiert, auch wenn sie später vom Zielmedium gelöscht werden. Eine manuelle Wiederausgabe wird über die WPF-GUI möglich sein.

## Aktueller Entwicklungsstand

- Windows-Dienst-Grundgerüst
- WPF-GUI-Grundgerüst
- gemeinsame Core-Bibliothek
- SQLite-Index unter `%ProgramData%\\KassenSync`
- konfigurierbarer Quellordner
- Unterordner optional
- konfigurierbare Dateiendungen
- `FileSystemWatcher` plus Sicherheits-Rescan
- Stabilitätsprüfung vor Hashing/Kopieren
- SHA-256-Indexierung
- eindeutige Erkennung über Dateiname + Hash
- festes Ziellaufwerk, z. B. `E:\\`
- frei wählbarer Ziel-Unterordner
- Übernahme der relativen Quellordnerstruktur
- Wartestatus bei fehlendem Zielmedium
- sicheres Kopieren über temporäre `.part`-Datei und anschließendes Ersetzen

## Geplant

- vollständige WPF-Verwaltungsoberfläche
- manuelle Wiederausgabe einzelner oder markierter Dateien
- Topmost-Kopierfortschritt und 3-Sekunden-Erfolg-/Fehlermeldung
- GUI-Autostart
- Auto-Update direkt über GitHub Releases
- Installer und GitHub-Actions-Releasepipeline

## Architektur

- `KassenSync.Core`: Modelle, Datenbank, Einstellungen, Hashing und Kopierfunktionen
- `KassenSync.Service`: Windows-Dienst, Watcher, Indexierung und Kopierwarteschlange
- `KassenSync.App`: WPF-GUI

Der Windows-Dienst zeigt selbst keine Fenster. Später werden Fortschritts- und Statusereignisse über eine Named Pipe an die WPF-GUI übertragen.
