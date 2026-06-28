# SC Log Reader

Live-Auswertung der Star-Citizen `Game.log`: Standort, Geld (rein/raus/Handel/Käufe),
Aufträge, Baupläne, Schiffe/Flotte, Crew, Tode, Ausrüstung u.v.m. — mit Filter-Chips,
Geld-Statistik, mitlaufendem Saldo und JSON/CSV-Export.
Cross-platform (Avalonia / .NET 8), Windows-Single-`.exe`.

> **Inoffizielles Community-/Fan-Projekt.** Nicht mit Cloud Imperium Games oder
> Roberts Space Industries verbunden, von ihnen unterstützt oder autorisiert.
> Liest **nur lokal** die `Game.log` (read-only) – kein Eingriff ins Spiel,
> kein Memory-Zugriff, AntiCheat-konform. Siehe [Disclaimer](#disclaimer).

## Download
Neueste `SCLogReader.exe` unter **[Releases](https://github.com/miwidot/SCLogReader/releases/latest)**
— eine Datei, kein Setup, kein .NET nötig. Das Tool prüft beim Start automatisch auf
Updates und bietet sie per Knopfdruck an (Auto-Updater über GitHub Releases).

## Bauen
```
dotnet build -c Release
```

## GUI starten
```
dotnet run -c Release
```
Pfad wird beim Start **automatisch erkannt** (alle Laufwerke, Channels LIVE/PTU/EPTU/…).
Mit **Auto** neu suchen. **Start** liest live mit, während SC läuft (Shared-Read,
stört das Spiel nicht), erkennt Log-Rotation beim Neustart.
**CSV/JSON exportieren** speichert die aktuelle Event-Liste (chronologisch).

## CLI-Batch (alte Logs auswerten)
```
SCLogReader.exe --scan "C:\Program Files\Roberts Space Industries\StarCitizen\LIVE"
```
Gibt Summen (Eingänge/Ausgänge/Rewards/Netto) + letzten Standort + letztes Lager aus.

## Was erkannt wird
| Typ | Quelle im Log |
|-----|----------------|
| Geld-Eingang | `Überweisung erhalten von: <name>` + Folgezeile `<betrag> aUEC` |
| Geld-Ausgang | `Sie haben <name> gesendet:` + Folgezeile `<betrag> aUEC` |
| Missions-Belohnung | `<betrag> aUEC erhalten` |
| Standort | `RequestLocationInventory ... Location[<id>]` |
| Lager | `Inventory[<id>] ... Item Count:[<n>]` |
| Schiff | `ClearDriver ... token for '<schiff>'` |
| Quantum | `<Quantum Drive Arrived ...>` — abgeschlossene Sprünge inkl. Schiff |
| Kauf | `SShopBuyRequest ... client_price itemClassGUID itemName quantity` |
| Verkauf | `SShopSellRequest ... client_price itemClassGUID itemName quantity` (Item) |
| Handel | `SShopCommoditySellRequest ... amount resourceGUID quantity` (Fracht/Waren) |

Item-Namen (Kauf/Verkauf) werden live über die **UEX-API**
(`api.uexcorp.uk/2.0/items?uuid=<guid>`) aufgelöst — die `itemClassGUID` aus dem
Log matcht direkt das UEX-`uuid`-Feld (z.B. `grin_tractor_01` → „MaxLift Tractor Beam").

Fracht-`resourceGUID` → Warenname über eine eingebackene Tabelle
(`Core/CommoditiesData.cs`, ~750 Waren, auto-generiert aus **scunpacked** via
`tools/gen-commodities.ps1`). Offline, kein Key.

Geld-Buckets: Einnahmen = Transfers rein + Belohnungen + Verkäufe + Handel;
Ausgaben = Transfers raus + Käufe. Verkäufe/Käufe sind Shop-*Requests* (mit
`result[Success]`/`type[Selling]` bestätigt), können also Wiederholungen enthalten.

## Lesbare Namen
- `Core/Locations.cs` übersetzt Location-Codes → echte Station-Namen
  (`RR_HUR_L4` → „HUR-L4 Melodic Fields Station", `RR_HUR_LEO` → „Everus Harbor · Hurston").
  Unbekannte Codes werden über Heuristiken (Jumppunkt/Lagrange/LEO) oder lesbar aufbereitet.
- `Core/Ships.cs` übersetzt Schiffs-Codes → Marke + Modell
  (`RSI_Hermes_…` → „Hermes · RSI").

## Bekannte Grenzen (Log-bedingt, nicht behebbar)
- Lager nur als **Stückzahl**, kein Warenname / kein SCU.
- Quantum: nur **Ankunft** (Sprung abgeschlossen) sicher erkennbar; **Zielnamen**
  stehen nicht im Log (nur Schiff + zuletzt bekannter Standort als Kontext).
- **Flotte/Schiffslager**: Die ASOP-Abfrage (`VehicleListQuery`) liefert nur eine
  *Anzahl* („2 von 3 Fahrzeugen"), KEINE Namen/Standorte. Eine „wo steht welches
  Schiff"-Liste ist serverseitig und steht NICHT im Client-Log. Erkennbar ist nur
  das aktuell geflogene Schiff + dein Standort.

## Struktur
- `Core/LogTailer.cs` — Live-Lesen mit `FileShare.ReadWrite`, Rotations-Erkennung
- `Core/LogParser.cs` — zustandsbehaftetes Zeilen-Parsing (Regex)
- `Core/Database.cs` — SQLite-Index der Sessions (+ `LogArchive.cs` Roh-Log-Archiv)
- `ViewModels/MainViewModel.cs` — MVVM, Summen + Live-Liste
- `Views/MainWindow.axaml` — UI (Übersicht + DataGrid)

## Disclaimer
Dies ist ein **inoffizielles, von Fans erstelltes Community-Tool** und steht in
**keiner Verbindung** zu Cloud Imperium Games (CIG) oder der Roberts Space
Industries Group of Companies (RSI) und wird von diesen weder unterstützt noch
gesponsert noch autorisiert.

„Star Citizen", „Squadron 42", „Roberts Space Industries" und „Cloud Imperium"
sind Marken der jeweiligen Inhaber. Alle Spiel-Inhalte und -Materialien gehören CIG.

Das Tool liest ausschließlich die **lokale `Game.log`** (read-only) zur Auswertung
deiner **eigenen** Spielsitzungen. Es verändert das Spiel nicht, greift nicht in
den Spielprozess ein und liest keinen Speicher — daher AntiCheat-konform.
Nutzung auf **eigene Verantwortung**, ohne jegliche Gewähr.

Externe Daten: Item-Namen via [UEX](https://uexcorp.space) (API),
Waren-/Spieldaten via [scunpacked](https://github.com/StarCitizenWiki/scunpacked-data).

## Lizenz
[MIT](LICENSE) — frei nutzbar, anpassbar und teilbar (mit Namensnennung).
Beiträge willkommen.
