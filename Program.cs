using System;
using System.IO;
using System.Linq;
using Avalonia;
using SCLogReader.Core;
using SCLogReader.Models;

namespace SCLogReader;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Deutsche Zahlenformatierung (Tausenderpunkte) für die Anzeige.
        var de = new System.Globalization.CultureInfo("de-DE");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = de;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = de;
        System.Threading.Thread.CurrentThread.CurrentCulture = de;

        // CLI-Modus:  SCLogReader.exe --scan <Datei-oder-Verzeichnis>
        if (args.Length >= 1 && args[0] == "--scan")
        {
            Scan(args.Length >= 2 ? args[1] : ".");
            return;
        }

        Core.Logger.Log($"GUI-Start · {Environment.OSVersion}");
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Core.Logger.Error("FATAL", ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();

    static void Scan(string target)
    {
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.log", SearchOption.AllDirectories).OrderBy(f => f).ToArray()
            : new[] { target };

        var all = new System.Collections.Generic.List<Sess>();
        foreach (var file in files)
            all.Add(ScanFile(file));

        // chronologisch (älteste zuerst)
        foreach (var s in all.OrderBy(s => s.Start))
        {
            Console.WriteLine($"━━ Session {s.Start.ToLocalTime():dd.MM. HH:mm} → {s.End.ToLocalTime():HH:mm}  [{Dur(s.End - s.Start)}]  ({s.Name})");
            Console.WriteLine($"   Einnahmen {s.Income:N0}  ·  Ausgaben {s.Spend:N0}  ·  NETTO {s.Net:N0} aUEC");
            Console.WriteLine($"   Handel {s.Sales + s.Trade:N0}  (Item {s.Sales:N0} / Fracht {s.Trade:N0})  ·  Käufe {s.Spent:N0}");
            Console.WriteLine($"   Standort: {s.LastLoc ?? "—"}  ·  Schiffe: {(s.Ships.Count == 0 ? "—" : string.Join(", ", s.Ships))}");
            Console.WriteLine($"   Missionen {s.Missions} · Gebiete {s.Zones} · Party {s.PartyEv} · Med-Bett {s.Deaths} · Hangar {s.Hangars} · Ausrüstung {s.Loadout.Count}");
            Console.WriteLine($"   Verluste {s.Losses} · Kampfunfähig {s.Incaps} · Angebote {s.Offers} · Beschlagn. {s.Impounds} · Freunde {s.Friends} · Defekt {s.Defekt}");
            foreach (var mt in s.MissionTexts.Take(6)) Console.WriteLine($"   ◦ {mt}");
            if (s.Blueprints.Count > 0) Console.WriteLine($"   Baupläne: {string.Join(", ", s.Blueprints)}");
        }

        var meta = all.OrderByDescending(s => s.Start).FirstOrDefault()?.Meta;
        if (meta is { Count: > 0 })
        {
            Console.WriteLine(new string('─', 60));
            Console.WriteLine("SYSTEM/CHARAKTER (neueste Session):");
            foreach (var kv in meta) Console.WriteLine($"   {kv.Key,-10}: {kv.Value}");
        }

        // Gesamt über alle Sessions
        Console.WriteLine(new string('─', 60));
        var total = TimeSpan.FromTicks(all.Sum(s => (s.End - s.Start).Ticks));
        Console.WriteLine($"GESAMT ({all.Count} Sessions · Spielzeit {Dur(total)})");
        Console.WriteLine($"   Einnahmen {all.Sum(s => s.Income):N0}  ·  Ausgaben {all.Sum(s => s.Spend):N0}  ·  NETTO {all.Sum(s => s.Net):N0} aUEC");
        Console.WriteLine($"   Handel {all.Sum(s => s.Sales + s.Trade):N0}  ·  Käufe {all.Sum(s => s.Spent):N0}");
        Console.WriteLine("GRÖSSTE EINZEL-POSTEN:");
        foreach (var m in all.SelectMany(s => s.Money).OrderByDescending(m => System.Math.Abs(m.amt)).Take(8))
            Console.WriteLine($"   {m.amt,12:N0}  {m.label}");
    }

    class Sess
    {
        public string Name = "";
        public DateTime Start, End;
        public long In, Out, Reward, Spent, Sales, Trade;
        public int Missions, Zones, PartyEv, Deaths, Hangars, Losses, Incaps, Offers, Impounds, Friends, Defekt;
        public string? LastLoc;
        public readonly System.Collections.Generic.SortedSet<string> Ships = new();
        public readonly System.Collections.Generic.SortedSet<string> Loadout = new();
        public readonly System.Collections.Generic.SortedSet<string> Blueprints = new();
        public readonly System.Collections.Generic.List<(long amt, string label)> Money = new();
        public string? SampleMission;
        public readonly System.Collections.Generic.SortedSet<string> MissionTexts = new();
        public System.Collections.Generic.Dictionary<string, string> Meta = new();
        public long Income => In + Sales + Trade;
        public long Spend => Out + Spent;
        public long Net => Income - Spend;
    }

    static Sess ScanFile(string file)
    {
        var s = new Sess { Name = Path.GetFileName(file) };
        var parser = new LogParser();
        bool first = true;
        foreach (var line in ReadSharedLines(file))
        {
            var ts = TimeOf(line);
            if (ts is { } t) { if (first) { s.Start = t; first = false; } s.End = t; }

            var e = parser.Feed(line);
            if (e == null) continue;
            if (e.Kind is EventKind.TransferIn or EventKind.TransferOut or EventKind.MissionReward
                       or EventKind.Purchase or EventKind.Sale or EventKind.Trade)
                s.Money.Add((e.Amount, $"{e.KindText}: {e.Detail}"));
            switch (e.Kind)
            {
                case EventKind.TransferIn: s.In += e.Amount; break;
                case EventKind.TransferOut: s.Out += -e.Amount; break;
                case EventKind.MissionReward: s.In += e.Amount; s.Reward += e.Amount; break;
                case EventKind.Location: s.LastLoc = e.Detail; break;
                case EventKind.Purchase: s.Spent += -e.Amount; break;
                case EventKind.Sale: s.Sales += e.Amount; break;
                case EventKind.Trade: s.Trade += e.Amount; break;
                case EventKind.Mission: s.Missions++; s.SampleMission ??= e.Detail; s.MissionTexts.Add(e.Detail); break;
                case EventKind.Blueprint: s.Blueprints.Add(e.Detail); break;
                case EventKind.Jurisdiction: s.Zones++; break;
                case EventKind.Party: s.PartyEv++; break;
                case EventKind.MedBed: s.Deaths++; break;
                case EventKind.Hangar: s.Hangars++; break;
                case EventKind.Loadout: s.Loadout.Add(e.Detail); break;
                case EventKind.ShipLoss: s.Losses++; break;
                case EventKind.Death: s.Incaps++; break;
                case EventKind.Offer: s.Offers++; break;
                case EventKind.Impound: s.Impounds++; break;
                case EventKind.Friend: s.Friends++; break;
                case EventKind.Gear: s.Defekt++; break;
            }
            if (e.Ship != null) s.Ships.Add(e.Ship);
        }
        s.Meta = parser.Meta;
        return s;
    }

    static readonly System.Text.RegularExpressions.Regex TsRe =
        new(@"<(?<ts>\d{4}-\d{2}-\d{2}T[\d:.]+Z)>", System.Text.RegularExpressions.RegexOptions.Compiled);

    static DateTime? TimeOf(string line)
    {
        var m = TsRe.Match(line);
        return m.Success && DateTime.TryParse(m.Groups["ts"].Value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var dt)
            ? dt : null;
    }

    static string Dur(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m";

    // Liest auch Dateien, die SC gerade offen hält (Shared-Read).
    static System.Collections.Generic.IEnumerable<string> ReadSharedLines(string file)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        string? l;
        while ((l = sr.ReadLine()) != null)
            yield return l;
    }
}
