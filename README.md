# KassenSync

Windows-Anwendung aus WPF-GUI und Windows-Dienst. Der Dienst überwacht einen konfigurierbaren Quellordner, indexiert neue Dateien anhand Dateiname + SHA-256 und kopiert sie auf ein konfiguriertes Ziellaufwerk. Bereits ausgegebene Dateien werden nicht automatisch erneut kopiert, auch wenn sie vom Zielmedium gelöscht wurden.

## Geplante Kernfunktionen

- Windows-Dienst, unabhängig von Benutzeranmeldung
- WPF-GUI
- SQLite-Index unter `%ProgramData%\\KassenSync`
- konfigurierbarer Quellordner und optionale Unterordner
- konfigurierbarer Ziel-Laufwerksbuchstabe und Ziel-Unterordner
- Beibehaltung der relativen Quellstruktur
- konfigurierbare Dateiendungen
- Warteschlange, wenn das Zielmedium fehlt
- manuelle Wiederausgabe einzelner/mehrerer Dateien
- Topmost-Kopierfortschritt in der GUI
- GUI-Autostart
- Auto-Update über GitHub Releases

## Architektur

- `KassenSync.Core`: Modelle, Datenbank, Einstellungen und IPC-Vertrag
- `KassenSync.Service`: Windows-Dienst, Watcher/Index/Kopierwarteschlange/IPC-Server
- `KassenSync.App`: WPF-GUI und IPC-Client

Der Dienst zeigt keine Benutzeroberfläche. Fortschritts- und Statusereignisse werden über eine Named Pipe an die WPF-GUI übertragen.
