# Changelog

Alle nennenswerten Änderungen an diesem Projekt. Format nach
[Keep a Changelog](https://keepachangelog.com/de/), Versionen nach
[SemVer](https://semver.org/lang/de/).

## [Unreleased]

## [1.1.11] - 2026-07-01
### Hinzugefügt
- **Versions-Badge in der Kopfzeile ist klickbar** → öffnet Changelog/Releases.
- Releases sind jetzt **digital signiert** (Certum Open Source Code Signing) → weniger SmartScreen-Warnung, sichtbarer Herausgeber.
### Geändert
- Redundante **Baupläne-Leiste** entfernt — der Baupläne-Filter in der Tabelle zeigt jetzt (seit „alle Events laden") alle Baupläne mit Datum.

## [1.1.10] - 2026-07-01
### Hinzugefügt
- Event-Tabelle lädt jetzt **alle** Events (virtualisiert) → alle Filter über die komplette Historie vollständig, nicht mehr nur die letzten ~4000 Zeilen.
- Vollständige, deduplizierte **Bauplan-Anzeige** (über alle Sessions, direkt aus der DB).

## [1.1.9] - 2026-06-30
### Hinzugefügt
- **Handel je Ware**: Gesamt-SCU, Ø Preis/SCU und Erlös je Commodity im Geld-Stats-Tab.

## [1.1.8] - 2026-06-30
### Hinzugefügt
- Zähler für **abgeschlossene Aufträge** (`MissionEnded`) mit Hinweis, dass die Belohnung serverseitig läuft und nicht im Log steht.

## [1.1.7] - 2026-06-29
### Hinzugefügt
- **Datum** bei den größten Geld-Posten und neue Liste **„Letzte Geld-Bewegungen"** (neueste zuerst).

## [1.1.6] - 2026-06-29
### Behoben
- „Online nachschlagen" nutzt jetzt eine breite Suche (zu enge `site:`-Filter entfernt).

## [1.1.5] - 2026-06-29
### Hinzugefügt
- ~750 **Commodity-Namen offline** aus scunpacked (kein „Fracht [guid]" mehr).
- **Online nachschlagen** per Rechtsklick/Doppelklick.

## [1.1.4] - 2026-06-28
### Hinzugefügt
- **Versions-Badge** in der Kopfzeile.

## [1.1.3] - 2026-06-28
### Behoben
- „Alle Sessions" tailt die laufende `Game.log` jetzt **live** (Standort/Schiff/Geld aktualisieren in Echtzeit).

## [1.1.2] - 2026-06-28
### Hinzugefügt
- App-/Fenster-/**Tray-Icon** und **Single-Instance**-Schutz.

## [1.1.1] - 2026-06-28
### Behoben
- „Alle Sessions" lädt jetzt blitzschnell über **SQL-Summen** statt jedes Event abzuspielen.

## [1.1.0] - 2026-06-28
### Hinzugefügt
- **SQLite-Index** der Sessions + **Roh-Log-Archiv** (überlebt SC-Backup-Löschung).
- **Debug-Log** neben der .exe mit Liste unbekannter Event-Typen.

## [1.0.1] - 2026-06-28
### Hinzugefügt
- Update-Prüfung alle 6 Stunden (zusätzlich zum Start).

## [1.0.0] - 2026-06-28
### Hinzugefügt
- Erste öffentliche Version: Geld/Handel/Käufe, Aufträge mit Namen+Rang, Baupläne, Schiffe/Flotte, Crew, Tode, Ausrüstung, Quantum, Orte; Filter, Geld-Stats, Saldo, JSON/CSV-Export, Auto-Updater, Single-`.exe`.


