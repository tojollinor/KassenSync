# KassenSync

KassenSync ist eine Windows-Anwendung aus **WPF-GUI + Windows-Dienst**. Der Dienst überwacht einen konfigurierbaren Quellordner, indexiert neue Dateien anhand **Dateiname + SHA-256** und kopiert sie auf ein konfiguriertes Ziellaufwerk.

Bereits erfolgreich ausgegebene Dateien werden nicht automatisch erneut kopiert, auch wenn sie später vom Zielmedium gelöscht werden. Über die GUI können einzelne oder mehrere markierte Dateien bewusst erneut ausgegeben werden.

## Funktionen

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
- konfigurierbarer GUI-Autostart
- Topmost-WPF-Kopierfenster mit Live-Fortschritt
- drei Sekunden sichtbare Erfolg-/Fehlermeldung nach einem Kopiervorgang
- automatische oder manuelle Updateprüfung über GitHub Releases
- direkter Download von `KassenSync-Setup.exe` aus GitHub Releases
- SHA-256-Prüfung des heruntergeladenen Installers
- UAC-gestütztes Update mit sichtbarem Installationsfortschritt
- automatischer Neustart der App mit Update-Erfolgsmeldung

## Installer und Releases

Der Installer wird mit Inno Setup gebaut. Die GitHub-Actions-Releasepipeline veröffentlicht:

- `KassenSync-Setup.exe`
- `KassenSync-Setup.exe.sha256`

Ein Release kann durch einen Tag wie `v0.1.0` oder manuell über den Release-Workflow erzeugt werden.

## Architektur

- `KassenSync.Core`: Modelle, Datenbank, Einstellungen, Hashing und Kopierfunktionen
- `KassenSync.Service`: Windows-Dienst, Watcher, Indexierung, Kopierwarteschlange und IPC-Server
- `KassenSync.App`: WPF-GUI, IPC-Client und GitHub-Updater

Der Windows-Dienst zeigt selbst keine Fenster. Benutzerinteraktion findet ausschließlich in der WPF-GUI statt.
