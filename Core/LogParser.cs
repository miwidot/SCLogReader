using System;
using System.Globalization;
using System.Text.RegularExpressions;
using SCLogReader.Models;

namespace SCLogReader.Core;

/// <summary>
/// Stateful, line-by-line parser for the Star Citizen Game.log.
/// Geld-Überweisungen stehen über zwei Zeilen (Kopfzeile + Betrags-Zeile),
/// daher wird ein "pending" Zustand gehalten, bis die Betragszeile kommt.
/// Wiederholungen (UpdateNotificationItem) werden ignoriert, weil sie nicht
/// mit 'Added notification' beginnen.
/// </summary>
public class LogParser
{
    static readonly Regex Ts =
        new(@"^<(?<ts>\d{4}-\d{2}-\d{2}T[\d:.]+Z)>", RegexOptions.Compiled);

    static readonly Regex RecvHdr =
        new(@"Added notification ""Überweisung erhalten von:\s*(?<who>.+?)\s*$", RegexOptions.Compiled);

    static readonly Regex SentHdr =
        new(@"Added notification ""Sie haben\s+(?<who>.+?)\s+gesendet:", RegexOptions.Compiled);

    static readonly Regex Reward =
        new(@"Added notification ""(?<amt>[\d.,]+)\s*aUEC erhalten", RegexOptions.Compiled);

    static readonly Regex AmtLine =
        new(@"^<[^>]+>\s*(?<amt>[\d.,]+)\s*aUEC\s*$", RegexOptions.Compiled);

    static readonly Regex Loc =
        new(@"RequestLocationInventory.*Location\[(?<loc>[^\]]+)\]", RegexOptions.Compiled);

    static readonly Regex Inv =
        new(@"Inventory\[(?<inv>[^\]]+)\].*Item Count:\[(?<cnt>\d+)\]", RegexOptions.Compiled);

    static readonly Regex Veh =
        new(@"ClearDriver:.*token for '(?<ship>[^']+)'", RegexOptions.Compiled);

    // Echtes QT-Ereignis: abgeschlossener Sprung (1x pro Ankunft), inkl. Schiff.
    static readonly Regex QtArrive =
        new(@"(?<ship>[A-Za-z][A-Za-z0-9_]+?)_\d+\[\d+\]\|CSCItemNavigation::OnQuantumDriveArrived", RegexOptions.Compiled);

    // Kauf an einem Shop/Kiosk: Item, Preis, Shop, GUID.
    static readonly Regex Buy =
        new(@"SShopBuyRequest.*?shopName\[(?<shop>[^\]]*)\].*?client_price\[(?<price>[\d.]+)\].*?itemClassGUID\[(?<guid>[^\]]*)\].*?itemName\[(?<item>[^\]]*)\].*?quantity\[(?<qty>\d+)\]",
            RegexOptions.Compiled);

    // Item-Verkauf (gleiche Felder wie Kauf, aber SShopSellRequest).
    static readonly Regex Sell =
        new(@"SShopSellRequest.*?shopName\[(?<shop>[^\]]*)\].*?client_price\[(?<price>[\d.]+)\].*?itemClassGUID\[(?<guid>[^\]]*)\].*?itemName\[(?<item>[^\]]*)\].*?quantity\[(?<qty>\d+)\]",
            RegexOptions.Compiled);

    // Fracht-/Waren-Verkauf (Commodity): Gesamtbetrag + resourceGUID + Menge.
    static readonly Regex Commodity =
        new(@"SShopCommoditySellRequest.*?shopName\[(?<shop>[^\]]*)\].*?amount\[(?<amt>[\d.]+)\].*?resourceGUID\[(?<guid>[^\]]*)\].*?quantity\[(?<qty>\d+)\]",
            RegexOptions.Compiled);

    // Notification-Kopfzeile (einmal pro Ereignis): Text bis ':' , '"' oder Zeilenende.
    // (Manche Notifications sind mehrzeilig – z.B. Geld-Angebote – daher auch $.)
    static readonly Regex Notif =
        new(@"Added notification ""(?<txt>[^"":]+?)(?::|""|$)", RegexOptions.Compiled);

    // Getragene Ausrüstung.
    static readonly Regex Attach =
        new(@"AttachmentReceived> Player\[(?<p>[^\]]+)\] Attachment\[(?<item>[A-Za-z][^,]+),", RegexOptions.Compiled);

    // Auftrag/Contract – vollständiger Text (Name, Rang, Route), nicht am ':' abschneiden.
    static readonly Regex MissionLine =
        new(@"Added notification ""(?<full>(?:Neuer Auftrag|Auftrag (?:angenommen|abgeschlossen|geteilt|zurückgezogen)|Contract (?:Accepted|Complete)|New Objective)[^""]*)",
            RegexOptions.Compiled);

    // Blaupause erhalten (mit Namen).
    static readonly Regex BlueprintLine =
        new(@"Added notification ""Bauplan erhalten: (?<name>[^""]*)", RegexOptions.Compiled);

    // Abgeschlossene Mission (Server-Event, ohne Betrag) – eindeutig je mission_id.
    static readonly Regex MissionDone =
        new(@"<MissionEnded>.*mission_id (?<id>[0-9a-f-]+) - mission_state MISSION_STATE_COMPLETED", RegexOptions.Compiled);

    // Ausrüstung/Item defekt ("Dein X ist unbrauchbar").
    static readonly Regex GearBroke =
        new(@"Deaktivierung eingeleitet: Dein (?<item>[^""]+?) ist unbrauchbar", RegexOptions.Compiled);

    // Kill-Feed (Standard-SC-Format) – greift bei Combat.
    static readonly Regex KillLine =
        new(@"CActor::Kill: '(?<victim>[^']*)' \[\d+\] in zone '[^']*' killed by '(?<killer>[^']*)' \[\d+\] using '(?<weapon>[^']*)'",
            RegexOptions.Compiled);

    // Geld-Angebot (vor Annahme).
    static readonly Regex OfferRe =
        new(@"(?<who>[A-Za-z0-9_\- ]+?) möchte dir (?<amt>[\d.,]+) UEC senden", RegexOptions.Compiled);

    // Schiffsverlust durch Kollision.
    static readonly Regex Collision =
        new(@"Fatal Collision occured for vehicle (?<ship>[A-Za-z][A-Za-z0-9_]+?)_\d+", RegexOptions.Compiled);

    // Comm-Kanal eines Schiffs: [ <Schiff> : <Besitzer> ]
    static readonly Regex Channel =
        new(@"Kanal \[ (?<ship>.+?) : (?<owner>[^\]]+?) \]", RegexOptions.Compiled);

    static readonly System.Collections.Generic.HashSet<string> OwnNames =
        new(StringComparer.OrdinalIgnoreCase) { "MiwiDot", "miwi", "miwitv" };

    // Bußgeld gezahlt (mit Betrag) – echtes aUEC raus, fließt in den Saldo.
    static readonly Regex FineLine =
        new(@"Added notification ""Strafe gezahlt:\s*(?<amt>[\d.,]+)", RegexOptions.Compiled);

    // Begangene Straftat (Crimestat-Verlauf).
    static readonly Regex CrimeLine =
        new(@"Added notification ""Begangene Straftat:\s*(?<crime>[^""]+)", RegexOptions.Compiled);

    // Veredelungs-/Refinery-Auftrag abgeschlossen.
    static readonly Regex RefineryLine =
        new(@"Added notification ""Ein Auftrag zur Veredelung wurde abgeschlossen(?<txt>[^""]*)", RegexOptions.Compiled);

    // Verletzung/Lähmung festgestellt (Schweregrad + Körperteil + Behandlungsstufe).
    static readonly Regex InjuryLine =
        new(@"Added notification ""(?<txt>(?:Leichte|Mäßige|Schwere|Kritische|Teilweise) (?:Verletzung|Lähmung)[^""]*)", RegexOptions.Compiled);

    // Party-Mitglieder rein/raus (Name in der Folgezeile der Notification).
    static readonly Regex PartyJoin =
        new(@"(?<who>[A-Za-z0-9_\-]+) ist Party beigetreten", RegexOptions.Compiled);
    static readonly Regex PartyLeave =
        new(@"(?<who>[A-Za-z0-9_\-]+) ha(?:t|st)(?: die)? Party verlassen", RegexOptions.Compiled);

    string? _pendWho;
    int _pendDir;          // +1 = rein, -1 = raus
    DateTime _pendTime;

    string? _lastLoc;                       // für Quantum-Kontext
    DateTime _lastQt = DateTime.MinValue;   // Drosselung der QT-Marker
    string? _lastNotif;                     // gegen Notification-Spam
    string? _lastParty;                     // gegen Party-Spam (Wiederholungen)
    readonly System.Collections.Generic.HashSet<string> _loadoutSeen = new();
    readonly System.Collections.Generic.HashSet<string> _channelSeen = new();
    readonly System.Collections.Generic.HashSet<string> _gearSeen = new();
    readonly System.Collections.Generic.HashSet<string> _missionsDone = new();

    /// <summary>Session-Metadaten (Build, Hardware, Charakter, Shard, …).</summary>
    public System.Collections.Generic.Dictionary<string, string> Meta { get; } = new();

    /// <summary>Unbekannte Notification-Typen (Diagnose: was decken wir noch nicht ab?).</summary>
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Unknown = new();

    public LogEntry? Feed(string line)
    {
        CaptureMeta(line);

        // Eine offene Überweisung wird durch die nächste Betragszeile aufgelöst.
        if (_pendWho != null)
        {
            var a = AmtLine.Match(line);
            if (a.Success)
            {
                long amt = ParseAmt(a.Groups["amt"].Value);
                string who = _pendWho;
                int dir = _pendDir;
                _pendWho = null;
                return new LogEntry
                {
                    Time = _pendTime,
                    Kind = dir > 0 ? EventKind.TransferIn : EventKind.TransferOut,
                    Detail = dir > 0 ? $"von {who}" : $"an {who}",
                    Amount = dir * amt
                };
            }
            // andere Zeile dazwischen -> pending bleibt bestehen
        }

        var r = RecvHdr.Match(line);
        if (r.Success) { _pendWho = Clean(r.Groups["who"].Value); _pendDir = +1; _pendTime = ParseTs(line); return null; }

        var s = SentHdr.Match(line);
        if (s.Success) { _pendWho = Clean(s.Groups["who"].Value); _pendDir = -1; _pendTime = ParseTs(line); return null; }

        var rw = Reward.Match(line);
        if (rw.Success)
            return new LogEntry { Time = ParseTs(line), Kind = EventKind.MissionReward, Detail = "Missions-Belohnung", Amount = ParseAmt(rw.Groups["amt"].Value) };

        var by = Buy.Match(line);
        if (by.Success)
        {
            long price = (long)ParseDouble(by.Groups["price"].Value);
            int qty = int.TryParse(by.Groups["qty"].Value, out var q) ? q : 1;
            var shop = CleanShop(by.Groups["shop"].Value);
            var item = ItemNames.CleanFallback(by.Groups["item"].Value);
            var suffix = qty > 1 ? $"×{qty} · {shop}" : $"· {shop}";
            return new LogEntry
            {
                Time = ParseTs(line),
                Kind = EventKind.Purchase,
                Amount = -(price * qty),
                ItemRef = by.Groups["guid"].Value,
                Suffix = suffix,
                Detail = $"{item}  {suffix}"
            };
        }

        var se = Sell.Match(line);
        if (se.Success)
        {
            long price = (long)ParseDouble(se.Groups["price"].Value);
            int qty = int.TryParse(se.Groups["qty"].Value, out var q) ? q : 1;
            var shop = CleanShop(se.Groups["shop"].Value);
            var item = ItemNames.CleanFallback(se.Groups["item"].Value);
            var suffix = qty > 1 ? $"×{qty} · {shop}" : $"· {shop}";
            return new LogEntry
            {
                Time = ParseTs(line),
                Kind = EventKind.Sale,
                Amount = price * qty,
                ItemRef = se.Groups["guid"].Value,
                Suffix = suffix,
                Detail = $"{item}  {suffix}"
            };
        }

        var co = Commodity.Match(line);
        if (co.Success)
        {
            long amt = (long)ParseDouble(co.Groups["amt"].Value);
            int qty = int.TryParse(co.Groups["qty"].Value, out var q) ? q : 0;
            var shop = CleanShop(co.Groups["shop"].Value);
            var ware = Commodities.Resolve(co.Groups["guid"].Value);
            return new LogEntry
            {
                Time = ParseTs(line),
                Kind = EventKind.Trade,
                Amount = amt,
                Detail = $"{ware} ×{qty} SCU  · {shop}"
            };
        }

        var lo = Loc.Match(line);
        if (lo.Success)
        {
            _lastLoc = Locations.Resolve(lo.Groups["loc"].Value);
            return new LogEntry { Time = ParseTs(line), Kind = EventKind.Location, Detail = _lastLoc };
        }

        var iv = Inv.Match(line);
        if (iv.Success)
        {
            // rohe Inventar-ID durch den zuletzt bekannten Standort ersetzen
            var place = _lastLoc ?? "Lager";
            return new LogEntry { Time = ParseTs(line), Kind = EventKind.Inventory, Detail = $"{place}  ·  {iv.Groups["cnt"].Value} Item(s)" };
        }

        var ve = Veh.Match(line);
        if (ve.Success)
        {
            var ship = Ships.Prettify(ve.Groups["ship"].Value);
            return new LogEntry { Time = ParseTs(line), Kind = EventKind.Vehicle, Detail = ship, Ship = ship };
        }

        // Quantum-Reise: nur ABGESCHLOSSENE Sprünge (Ankunft). Das Log nennt
        // keine Zielnamen, daher Schiff + zuletzt bekannter Standort als Kontext.
        var qt = QtArrive.Match(line);
        if (qt.Success)
        {
            var t = ParseTs(line);
            var ship = Ships.Prettify(qt.Groups["ship"].Value);
            if ((t - _lastQt).TotalSeconds > 3)   // doppelte Logzeilen entprellen
            {
                _lastQt = t;
                return new LogEntry
                {
                    Time = t,
                    Kind = EventKind.Quantum,
                    Ship = ship,
                    Detail = _lastLoc is null ? $"QT-Ankunft · {ship}" : $"QT-Ankunft · {ship} (bei {_lastLoc})"
                };
            }
        }

        // Kill-Feed (Combat)
        var kl = KillLine.Match(line);
        if (kl.Success)
        {
            var victim = kl.Groups["victim"].Value;
            var killer = kl.Groups["killer"].Value;
            var weapon = ItemNames.CleanFallback(kl.Groups["weapon"].Value);
            string detail =
                OwnNames.Contains(victim) ? $"☠ getötet von {killer} ({weapon})" :
                OwnNames.Contains(killer) ? $"Kill: {victim} ({weapon})" :
                $"{killer} ✟ {victim} ({weapon})";
            return new LogEntry { Time = ParseTs(line), Kind = EventKind.Kill, Detail = detail };
        }

        // Schiffsverlust (Kollision) – zählt auch zur Flotte (dein Schiff)
        var fc = Collision.Match(line);
        if (fc.Success)
        {
            var ship = Ships.Prettify(fc.Groups["ship"].Value);
            return new LogEntry { Time = ParseTs(line), Kind = EventKind.ShipLoss, Detail = $"{ship} – Kollision", Ship = ship };
        }

        // Entitlement/Miete gestartet
        if (line.Contains("<EntitlementStarted>"))
            return new LogEntry { Time = ParseTs(line), Kind = EventKind.Entitlement, Detail = "Entitlement/Miete gestartet" };

        // Comm-Kanal -> eigene Schiffe in die Flotte, fremde als Party-Schiff
        var ch = Channel.Match(line);
        if (ch.Success)
        {
            var shipName = ch.Groups["ship"].Value.Trim();
            var owner = ch.Groups["owner"].Value.Trim();
            if (OwnNames.Contains(owner))
            {
                if (_channelSeen.Add("me|" + shipName))
                    return new LogEntry { Time = ParseTs(line), Kind = EventKind.Vehicle, Detail = shipName, Ship = shipName };
            }
            else if (_channelSeen.Add(shipName + "|" + owner))
            {
                return new LogEntry { Time = ParseTs(line), Kind = EventKind.Party, Detail = $"Schiff: {shipName} · {owner}" };
            }
            return null;
        }

        // Getragene Ausrüstung (einmal je Item)
        var at = Attach.Match(line);
        if (at.Success)
        {
            var name = CleanLoadout(at.Groups["item"].Value);
            if (name != null && _loadoutSeen.Add(name))
                return new LogEntry { Time = ParseTs(line), Kind = EventKind.Loadout, Detail = name };
            return null;
        }

        // Aufträge zuerst mit VOLLEM Text (Name/Rang/Route)
        var ms = MissionLine.Match(line);
        if (ms.Success)
        {
            var full = CleanMission(ms.Groups["full"].Value);
            if (full == _lastNotif) return null;
            _lastNotif = full;
            return new LogEntry { Time = ParseTs(line), Kind = EventKind.Mission, Detail = full };
        }

        // Party-Mitglied beigetreten / verlassen (mit Name)
        var pj = PartyJoin.Match(line);
        if (pj.Success)
        {
            var key = "j:" + pj.Groups["who"].Value;
            if (key == _lastParty) return null;
            _lastParty = key;
            return new LogEntry { Time = ParseTs(line), Kind = EventKind.Party, Detail = $"▸ {pj.Groups["who"].Value} ist beigetreten" };
        }
        var pl = PartyLeave.Match(line);
        if (pl.Success)
        {
            var who = pl.Groups["who"].Value;
            if (who.Equals("Du", StringComparison.OrdinalIgnoreCase)) who = "Du";
            var key = "l:" + who;
            if (key == _lastParty) return null;
            _lastParty = key;
            return new LogEntry { Time = ParseTs(line), Kind = EventKind.Party, Detail = $"◂ {who} hat verlassen" };
        }

        // Ausrüstung/Item defekt – jedes Item nur EINMAL (Warnung feuert sonst im Sekundentakt)
        var gb = GearBroke.Match(line);
        if (gb.Success)
        {
            var item = gb.Groups["item"].Value.Trim();
            if (item.Length > 0 && _gearSeen.Add(item))
                return new LogEntry { Time = ParseTs(line), Kind = EventKind.Gear, Detail = $"{item} unbrauchbar" };
            return null;
        }

        // Abgeschlossene Mission (Server-Event, ohne Betrag) – je mission_id nur einmal
        var md = MissionDone.Match(line);
        if (md.Success)
        {
            if (_missionsDone.Add(md.Groups["id"].Value))
                return new LogEntry { Time = ParseTs(line), Kind = EventKind.MissionDone, Detail = "Auftrag abgeschlossen (Belohnung serverseitig)" };
            return null;
        }

        // Blaupause erhalten (mit Namen)
        var bp = BlueprintLine.Match(line);
        if (bp.Success)
        {
            var name = bp.Groups["name"].Value.TrimEnd(' ', ':');
            if (name != _lastNotif) { _lastNotif = name; return new LogEntry { Time = ParseTs(line), Kind = EventKind.Blueprint, Detail = name }; }
            return null;
        }

        // Bußgeld gezahlt – echtes aUEC raus (fließt in den Saldo)
        var fn = FineLine.Match(line);
        if (fn.Success)
        {
            long amt = ParseAmt(fn.Groups["amt"].Value);
            return new LogEntry { Time = ParseTs(line), Kind = EventKind.Fine, Amount = -amt, Detail = $"Strafe gezahlt: {amt:N0} aUEC" };
        }

        // Begangene Straftat (Crimestat)
        var cr = CrimeLine.Match(line);
        if (cr.Success)
        {
            var crime = cr.Groups["crime"].Value.TrimEnd(' ', ':');
            return new LogEntry { Time = ParseTs(line), Kind = EventKind.Crime, Detail = crime };
        }

        // Veredelungs-Auftrag abgeschlossen (Refinery)
        var rf = RefineryLine.Match(line);
        if (rf.Success)
        {
            var where = rf.Groups["txt"].Value.Trim().TrimStart('.').Trim().TrimEnd('.');
            return new LogEntry { Time = ParseTs(line), Kind = EventKind.Refinery, Detail = where.Length > 0 ? $"Veredelung fertig {where}" : "Veredelung fertig" };
        }

        // Verletzung/Lähmung festgestellt (Körperteil + Behandlungsstufe)
        var ij = InjuryLine.Match(line);
        if (ij.Success)
        {
            var txt = ij.Groups["txt"].Value.Replace(" Behandlung erforderlich", "").Replace(" festgestellt", "").Trim().TrimEnd(' ', ':', '-');
            if (txt != _lastNotif) { _lastNotif = txt; return new LogEntry { Time = ParseTs(line), Kind = EventKind.Injury, Detail = txt }; }
            return null;
        }

        // Notifications -> Gebiete / Party / Med-Bett / Hangar / Gefängnis / Angebote
        var nt = Notif.Match(line);
        if (nt.Success)
        {
            var txt = nt.Groups["txt"].Value.Trim();
            if (txt == _lastNotif) return null;       // exakte Wiederholung überspringen
            _lastNotif = txt;

            var off = OfferRe.Match(txt);
            if (off.Success)
                return new LogEntry
                {
                    Time = ParseTs(line),
                    Kind = EventKind.Offer,
                    Amount = ParseAmt(off.Groups["amt"].Value),     // nur Anzeige, nicht in Bilanz
                    Detail = $"Angebot von {off.Groups["who"].Value.Trim()}"
                };

            var kind = Categorize(txt);
            if (kind != null)
                return new LogEntry { Time = ParseTs(line), Kind = kind.Value, Detail = txt };

            // unbekannte Notification -> für Diagnose merken (Debug-Log)
            Unknown.AddOrUpdate(txt, 1, (_, c) => c + 1);
        }

        return null;
    }

    static EventKind? Categorize(string t)
    {
        if (t.Contains("FREUND") || t.Contains("Freund hinzu")) return EventKind.Friend;
        if (t.Contains("beschlagnahmt") || t.Contains("Beschlagnahm")) return EventKind.Impound;
        if (t.Contains("Kampfunfähig") || t.Contains("Notfalldienste") || t.Contains("Incapacitat")) return EventKind.Death;
        if (t.StartsWith("Auftrag") || t.StartsWith("Neuer Auftrag") || t.Contains("Mission")) return EventKind.Mission;
        if (t.Contains("Klescher") || t.Contains("Gefängnis") || t.Contains("Haftstrafe") ||
            t.Contains("Rehabilitation") || t.Contains("Kopfgeld") || t.Contains("Verbrechen") ||
            t.Contains("CrimeStat") || t.Contains("inhaftiert")) return EventKind.Jurisdiction;
        if (t.StartsWith("Rechtsgebiet") || t.StartsWith("Kontrollierten Raum") ||
            t.StartsWith("Schutzzone") || t.Contains("Armistice")) return EventKind.Jurisdiction;
        if (t.StartsWith("Partystart") || t.Contains("GRUPPE") || t.Contains("Gruppenanführer")) return EventKind.Party;
        if (t.Contains("Krankenbett")) return EventKind.MedBed;
        if (t.StartsWith("Hangar")) return EventKind.Hangar;
        return null;
    }

    static readonly string[] LoadoutNoise =
    {
        "Default_", "FPS_Default", "Head_", "Shared_", "FP_Visor", "LensDisplay", "Eyedetail",
        "Eyelash", "necksock", "brows_", "hair_", "Inventory_LocalAttach", "Scalp", "Teeth",
        "_LensDisplay", "Skin", "Beard", "Mouth", "PuglioseSkin"
    };

    static string CleanMission(string s)
    {
        s = Regex.Replace(s, @"<EM4>.*?</EM4>", "");                         // Blueprint-Marker-Block ganz raus
        s = Regex.Replace(s, @"<[^>]*>", "");                                 // restliche Tags
        // unaufgelöste Platzhalter-Segmente (geteilte Aufträge) entfernen
        s = Regex.Replace(s, @"\s*Rang:\s*~mission\([^)]*\)\s*\|?", "");
        s = Regex.Replace(s, @"\s*Direktroute:\s*~mission\([^)]*\)\s*-?", "");
        s = Regex.Replace(s, @"~mission\([^)]*\)", "");
        s = s.Replace("[BP]", "").Trim(' ', ':', '|', '*', '?', '-');
        return Regex.Replace(s, @"\s{2,}", " ").Trim();
    }

    static string? CleanLoadout(string raw)
    {
        foreach (var n in LoadoutNoise)
            if (raw.Contains(n)) return null;
        var name = Regex.Replace(raw, @"_\d{4,}$", "");
        if (name.EndsWith("_mag")) return null;     // Magazine ausblenden
        name = name.Replace('_', ' ').Trim();
        return name.Length < 3 ? null : name;
    }

    void CaptureMeta(string line)
    {
        if (!Meta.ContainsKey("version") && line.Contains("FileVersion:"))
            Meta["version"] = After(line, "FileVersion:");
        if (!Meta.ContainsKey("cpu") && line.Contains("Host CPU:"))
            Meta["cpu"] = After(line, "Host CPU:");
        if (!Meta.ContainsKey("env") && line.Contains("[Trace] Environment:"))
            Meta["env"] = After(line, "Environment:");
        if (!Meta.ContainsKey("gpu"))
        {
            var m = Regex.Match(line, @"- (?<g>(NVIDIA|AMD|Intel)[^(]+?) \(vendor");
            if (m.Success) Meta["gpu"] = m.Groups["g"].Value.Trim();
        }
        if (!Meta.ContainsKey("ram"))
        {
            var m = Regex.Match(line, @"(?<mb>\d+)MB physical memory installed");
            if (m.Success && long.TryParse(m.Groups["mb"].Value, out var mb)) Meta["ram"] = $"{mb / 1024} GB";
        }
        if (!Meta.ContainsKey("character"))
        {
            var m = Regex.Match(line, @"name (?<n>\S+) - state STATE_CURRENT");
            if (m.Success) Meta["character"] = m.Groups["n"].Value;
        }
        if (!Meta.ContainsKey("shard"))
        {
            var m = Regex.Match(line, @"Join PU>.*shard\[(?<s>[^\]]+)\]");
            if (m.Success) Meta["shard"] = m.Groups["s"].Value;
        }
    }

    static string After(string line, string key)
    {
        var i = line.IndexOf(key, StringComparison.Ordinal);
        return i < 0 ? "" : line[(i + key.Length)..].Trim();
    }

    static long ParseAmt(string s) =>
        long.TryParse(s.Replace(".", "").Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    static double ParseDouble(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    static string CleanShop(string s)
    {
        s = Regex.Replace(s, "^SCShop_", "");
        return s.Replace('_', ' ').Trim();
    }

    static DateTime ParseTs(string line)
    {
        var m = Ts.Match(line);
        if (m.Success && DateTime.TryParse(m.Groups["ts"].Value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
            return dt;
        return DateTime.UtcNow;
    }

    static string Clean(string s) => s.Trim().TrimEnd('"').Trim();
}
