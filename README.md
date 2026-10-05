# OrdnerSync

OrdnerSync ist eine Windows-Anwendung aus **WPF-GUI + Windows-Dienst**. Der Dienst überwacht einen konfigurierbaren Quellordner, indexiert neue Dateien anhand **Dateiname + SHA-256** und kopiert sie auf ein konfiguriertes Zielmedium.

Bereits erfolgreich ausgegebene Dateien werden nicht automatisch erneut kopiert, auch wenn sie später vom Zielmedium gelöscht werden. Über Checkboxen können einzelne oder mehrere Dateien bewusst erneut ausgegeben werden.

## Funktionen

- Windows-Dienst und WPF-GUI
- SQLite-Index unter `%ProgramData%\\OrdnerSync`
- automatische Migration des bisherigen `%ProgramData%\\KassenSync`-Datenbestands
- konfigurierbarer Quellordner und optionale Unterordner
- konfigurierbare Dateiendungen
- `FileSystemWatcher` plus Sicherheits-Rescan
- Stabilitätsprüfung vor Hashing/Kopieren
- SHA-256-Indexierung über Dateiname + Hash
- frei wählbares Ziellaufwerk und Ziel-Unterordner
- Übernahme der relativen Quellordnerstruktur
- Wartestatus bei fehlendem Zielmedium
- sicheres Kopieren über temporäre `.part`-Datei
- Checkbox-Auswahl mit manueller Wiederausgabe
- Dienststatus und Dienststeuerung in den Einstellungen
- konfigurierbarer GUI-Autostart
- Topmost-Kopierfortschritt und Ergebnisanzeige
- Über-Seite und OrdnerSync-Branding
- automatische oder manuelle Updateprüfung über GitHub Releases
- Installer und Updates direkt aus `tojollinor/OrdnerSync`

## Upgrade von KassenSync 0.1.0

OrdnerSync 0.2.0 übernimmt den bisherigen Index und die Einstellungen. Der Release stellt zusätzlich KassenSync-kompatible Assetnamen bereit, damit die bereits installierte 0.1.0 das Rename-Update automatisch laden kann.

## Architektur

Die bestehenden internen .NET-Namespaces bleiben aus Kompatibilitätsgründen vorerst `KassenSync.*`. Die ausgelieferten Assemblies, Oberfläche, Dienstidentität, Datenpfade und Installer heißen **OrdnerSync**.
