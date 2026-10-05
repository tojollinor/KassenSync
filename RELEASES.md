# Releases

## 0.3.2

- sichtbares Logo in Hauptfenster und Über-Seite vollständig als Vektorgrafik umgesetzt
- defekte ICO-Darstellung beeinflusst die GUI nicht mehr
- Dienst-Autostart in den Einstellungen aktivierbar/deaktivierbar
- deaktivierter Dienst-Autostart bedeutet manueller Start, nicht deaktivierter Dienst
- frei wählbare Hintergrundfarbe für OrdnerSync-Fenster und Meldungen
- Farbauswahl per Dialog, HEX-Feld und Standard-Schaltfläche
- neues Standard-Theme in hellem Blau (#EAF6FF)


## 0.3.1

- Darstellungsfehler bei der Anzeigedauer behoben
- Jobfilter sauber ausgerichtet und mit stabiler Höhe dargestellt
- Autostart-Beschriftung eindeutig als GUI-/Fenster-Autostart formuliert
- Hinweis ergänzt, dass der Windows-Dienst unabhängig automatisch startet
- Updateprüfung sprachlich als Start der Oberfläche präzisiert
- Logo-Rendering wählt passende ICO-Ebenen statt der fehlerhaften größten Ebene


## 0.3.0

- mehrere unabhängige Sync-Jobs
- Jobs aktiv/inaktiv
- normale Ordner oder USB-Sticks als Ziel
- Einweg- und bidirektionale Synchronisation
- SHA-256-basierter gemeinsamer Sync-Stand und Konflikterkennung
- keine automatische Löschweitergabe
- persistente USB-Wartezustände mit „Später“ sowie „Ja/Nein“ beim Wiederverbinden
- persistente Fehlermeldungen mit großem OK
- Erfolgsmeldungen: Aus, zeitgesteuert oder persistent mit Bestätigung
- frei einstellbare Dauer zeitgesteuerter Erfolgsmeldungen von 1 bis 60 Sekunden
- Jobfilter in der Dateiübersicht
- zentrale Kopf-Checkbox für alle sichtbaren Einträge
- markierte Indexeinträge löschbar, ohne Quelldateien anzutasten
- farbige Statuspunkte für Dateien, Jobs und Dienst
- Infotray mit Weiterlauf der GUI
- verbessertes Logo-Rendering in Hauptfenster und Über-Seite
- zusätzliche SHA-256-Verifikation der Zielkopie vor dem finalen Ersetzen
- Migration des bisherigen Einzeljobs und Dateiindexes auf die Mehrjob-Struktur

## 0.2.0

- Umbenennung von KassenSync in OrdnerSync
- Übernahme des bisherigen Dateiindexes und der Einstellungen
- OrdnerSync-Logo als App-, Fenster- und Installer-Branding
- stabile Checkbox-Auswahl für einzelne Dateien
- Über-Seite
- Dienststatus sowie Start/Stop/Neustart in den Einstellungen
- Auto-Update auf `tojollinor/OrdnerSync`
