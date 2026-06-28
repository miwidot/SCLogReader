using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Collections;
using Avalonia.Media;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCLogReader.Core;
using SCLogReader.Models;
using SCLogReader.Services;

namespace SCLogReader.ViewModels;

public partial class MainViewModel : ObservableObject
{
    LogTailer? _tailer;
    LogParser _parser = new();
    bool _initializing;
    bool _ready;          // Persistenz erst nach Konstruktor
    bool _suppressSave;   // Session-Wechsel nicht als Default speichern
    DateTime? _sessionStart, _sessionEnd;
    AppSettings _settings = new();

    [ObservableProperty] private string logPath = "";
    [ObservableProperty] private string status = "bereit";
    [ObservableProperty] private string manualBalance = "";   // echter Kontostand (Eingabe)
    [ObservableProperty] private bool updateAvailable;
    [ObservableProperty] private string updateText = "";
    Updater.Info? _update;
    [ObservableProperty] private SessionInfo? selectedSession;
    [ObservableProperty] private string currentLocation = "—";
    [ObservableProperty] private string currentShip = "—";
    [ObservableProperty] private string lastInventory = "—";

    [ObservableProperty] private long totalIn;        // Transfers rein
    [ObservableProperty] private long totalReward;    // Missions-Belohnungen
    [ObservableProperty] private long totalOut;       // Transfers raus
    [ObservableProperty] private long totalPurchases; // Käufe
    [ObservableProperty] private long totalSales;     // Item-Verkäufe
    [ObservableProperty] private long totalTrade;     // Fracht/Waren-Verkäufe

    [ObservableProperty] private bool running;

    public ObservableCollection<LogEntry> Events { get; } = new();
    public ObservableCollection<SessionInfo> Sessions { get; } = new();

    // Gefilterte Ansicht für das DataGrid (Filter-Chips + Spalten-Sortierung)
    public DataGridCollectionView EventsView { get; }

    [ObservableProperty] private string activeFilter = "Alle";
    HashSet<EventKind>? _activeKinds;

    static readonly Dictionary<string, HashSet<EventKind>?> FilterMap = new()
    {
        ["Alle"] = null,
        ["Geld"] = new() { EventKind.TransferIn, EventKind.TransferOut, EventKind.MissionReward,
                           EventKind.Purchase, EventKind.Sale, EventKind.Trade, EventKind.Offer },
        ["Aufträge"] = new() { EventKind.Mission },
        ["Baupläne"] = new() { EventKind.Blueprint },
        ["Schiffe"] = new() { EventKind.Vehicle, EventKind.Quantum, EventKind.ShipLoss },
        ["Orte"] = new() { EventKind.Location, EventKind.Jurisdiction, EventKind.Hangar },
        ["Crew"] = new() { EventKind.Party, EventKind.Friend },
        ["Sonst"] = new() { EventKind.MedBed, EventKind.Death, EventKind.Impound,
                            EventKind.Loadout, EventKind.Entitlement, EventKind.Inventory, EventKind.Gear, EventKind.Kill },
    };

    [RelayCommand]
    private void SetBalance()
    {
        var v = StartBalance();
        ManualBalance = v > 0 ? v.ToString("N0") : "";   // mit Tausenderpunkten anzeigen
        _settings.Balance = v;
        Settings.Save(_settings);
        RecomputeBalances();
        OnPropertyChanged(nameof(AccountText));
        Status = v > 0 ? $"Kontostand gesetzt: {v:N0} aUEC" : "Kontostand geleert";
    }

    [RelayCommand]
    private void SetFilter(string name)
    {
        ActiveFilter = name;
        _activeKinds = FilterMap.TryGetValue(name, out var k) ? k : null;
        EventsView.Refresh();
    }

    public string SessionSpanText =>
        _sessionStart is { } a && _sessionEnd is { } b
            ? $"{a.ToLocalTime():dd.MM. HH:mm} → {b.ToLocalTime():HH:mm}   ({Dur(b - a)})"
            : "—";

    static string Dur(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m";

    readonly System.Collections.Generic.HashSet<string> _shipSet = new();
    public ObservableCollection<string> ShipsSeen { get; } = new();
    public string FleetText => ShipsSeen.Count == 0 ? "" : $"{ShipsSeen.Count}× geflogen";
    public string ShipsSeenText => ShipsSeen.Count == 0 ? "—" : string.Join("\n", ShipsSeen);

    public long IncomeAll => TotalIn + TotalReward + TotalSales + TotalTrade;
    public long SpendAll => TotalOut + TotalPurchases;
    public long NetAll => IncomeAll - SpendAll;

    // Geld-Statistik (eigener Tab)
    public ObservableCollection<StatItem> IncomeStats { get; } = new();
    public ObservableCollection<StatItem> SpendStats { get; } = new();
    public ObservableCollection<StatItem> TopTransactions { get; } = new();
    public string IncomeTotalText => $"{IncomeAll:N0} aUEC";
    public string SpendTotalText => $"{SpendAll:N0} aUEC";

    const double BarMax = 280.0;

    void RebuildStats()
    {
        var inc = new (string L, long V, string C)[]
        {
            ("Transfers rein", TotalIn,    "#4ADE80"),
            ("Belohnungen",    TotalReward, "#FBBF24"),
            ("Item-Verkäufe",  TotalSales,  "#34D399"),
            ("Fracht-Handel",  TotalTrade,  "#22D3EE"),
        };
        var spd = new (string L, long V, string C)[]
        {
            ("Transfers raus", TotalOut,       "#F87171"),
            ("Käufe",          TotalPurchases, "#FB923C"),
        };

        long max = 1;
        foreach (var x in inc) max = System.Math.Max(max, x.V);
        foreach (var x in spd) max = System.Math.Max(max, x.V);

        IncomeStats.Clear();
        foreach (var x in inc.Where(i => i.V > 0).OrderByDescending(i => i.V))
            IncomeStats.Add(new StatItem { Label = x.L, Value = x.V, BarWidth = x.V / (double)max * BarMax, Color = Brush(x.C) });

        SpendStats.Clear();
        foreach (var x in spd.Where(i => i.V > 0).OrderByDescending(i => i.V))
            SpendStats.Add(new StatItem { Label = x.L, Value = x.V, BarWidth = x.V / (double)max * BarMax, Color = Brush(x.C) });

        TopTransactions.Clear();
        // Top 8 nach Größe wählen, aber aufsteigend anzeigen (Größter unten)
        var top = Events.Where(e => IsMoney(e.Kind))
                        .OrderByDescending(e => System.Math.Abs(e.Amount))
                        .Take(8)
                        .OrderBy(e => System.Math.Abs(e.Amount));
        foreach (var e in top)
            TopTransactions.Add(new StatItem
            {
                Label = $"{e.KindText}: {e.Detail}",
                Value = e.Amount,
                BarWidth = System.Math.Abs(e.Amount) / (double)max * BarMax,
                Color = Brush(e.Amount >= 0 ? "#4ADE80" : "#F87171")
            });

        OnPropertyChanged(nameof(IncomeTotalText));
        OnPropertyChanged(nameof(SpendTotalText));
    }

    static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    public string MetaSummary
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (_parser.Meta.TryGetValue("character", out var c)) parts.Add(c);
            if (_parser.Meta.TryGetValue("version", out var v)) parts.Add("v" + v);
            if (_parser.Meta.TryGetValue("shard", out var s)) parts.Add(s);
            return parts.Count == 0 ? "Star Citizen · Live-Auswertung" : string.Join("  ·  ", parts);
        }
    }

    // Log-Bewegung mit Vorzeichen (grün/rot via Converter im XAML)
    public string NetBalanceText => $"Log-Bewegung {NetAll:+#,##0;-#,##0;0} aUEC";
    public long NetSign => NetAll;
    public string FlowText => $"▼ Ein {IncomeAll:N0}    ▲ Aus {SpendAll:N0}";
    public string TradeText => $"⇄ Handel {TotalSales + TotalTrade:N0}    ↧ Käufe {TotalPurchases:N0}";
    public string ToggleText => Running ? "Stop" : "Start";

    // Echter Kontostand (Eingabe) -> formatiert
    public string AccountText
    {
        get
        {
            var digits = new string(ManualBalance.Where(char.IsDigit).ToArray());
            return long.TryParse(digits, out var v) && v > 0 ? $"{v:N0} aUEC" : "— eintragen —";
        }
    }

    long _running;

    long StartBalance()
    {
        var digits = new string(ManualBalance.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, out var v) ? v : 0;
    }

    public long ExpectedBalance => StartBalance() + NetAll;
    public string ExpectedText => StartBalance() > 0 ? $"≈ {ExpectedBalance:N0} aUEC" : "Start oben eintragen";

    static bool IsMoney(EventKind k) => k is EventKind.TransferIn or EventKind.TransferOut
        or EventKind.MissionReward or EventKind.Purchase or EventKind.Sale or EventKind.Trade;

    // Saldo-Verlauf neu berechnen (z.B. wenn Startwert geändert wird)
    void RecomputeBalances()
    {
        long run = StartBalance();
        for (int i = Events.Count - 1; i >= 0; i--)   // ältestes zuerst
        {
            var e = Events[i];
            if (!IsMoney(e.Kind)) continue;
            run += e.Amount;
            e.BalanceAfter = run;
            e.HasBalance = true;
        }
        _running = run;
        OnPropertyChanged(nameof(ExpectedText));
        OnPropertyChanged(nameof(ExpectedBalance));
    }

    partial void OnManualBalanceChanged(string value)
    {
        OnPropertyChanged(nameof(AccountText));
        RecomputeBalances();
        if (!_ready) return;
        _settings.Balance = StartBalance();
        Settings.Save(_settings);
    }

    public MainViewModel()
    {
        EventsView = new DataGridCollectionView(Events)
        {
            Filter = o => _activeKinds == null || (o is LogEntry e && _activeKinds.Contains(e.Kind))
        };

        // Gemerkte Einstellungen laden (Pfad + Kontostand), sonst automatisch suchen.
        _settings = Settings.Load();
        var saved = _settings.LogPath;
        var start = !string.IsNullOrWhiteSpace(saved) && File.Exists(saved)
            ? saved!
            : PathFinder.FindBest() ?? @"C:\Program Files\Roberts Space Industries\StarCitizen\LIVE\Game.log";

        LogPath = start;
        if (_settings.Balance > 0) ManualBalance = _settings.Balance.ToString("N0");
        RefreshSessions(selectCurrent: true);
        Status = saved != null && start == saved
            ? "gemerkte Einstellungen geladen"
            : Sessions.Count > 0 ? $"{Sessions.Count} Sessions gefunden" : "keine Game.log gefunden";
        _ready = true;

        _settings.LogPath = LogPath;
        Settings.Save(_settings);

        // Standard: gleich alle Sessions laden
        if (SelectedSession?.IsAll == true) LoadSession();

        CheckForUpdate();
    }

    async void CheckForUpdate()
    {
        var info = await Updater.CheckAsync();
        if (info is null) return;
        Dispatcher.UIThread.Post(() =>
        {
            _update = info;
            UpdateAvailable = true;
            UpdateText = $"⬆ Update {info.Version}";
        });
    }

    [RelayCommand]
    private async Task Update()
    {
        if (_update is null) return;
        Status = $"lade Update {_update.Version}…";
        await Updater.ApplyAsync(_update);
        Status = "Update wird installiert – Neustart…";
        await Task.Delay(400);
        (Avalonia.Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    partial void OnRunningChanged(bool value) => OnPropertyChanged(nameof(ToggleText));

    // Pfad wird gemerkt – aber nicht bei reinen Session-Wechseln.
    partial void OnLogPathChanged(string value)
    {
        if (!_ready || _suppressSave) return;
        _settings.LogPath = value;
        Settings.Save(_settings);
    }

    partial void OnSelectedSessionChanged(SessionInfo? value)
    {
        if (_initializing || value is null) return;
        if (!value.IsAll)
        {
            _suppressSave = true;
            LogPath = value.Path;
            _suppressSave = false;
        }
        LoadSession();   // Session wechseln: zurücksetzen + einlesen
    }

    void RefreshSessions(bool selectCurrent)
    {
        var found = SessionScanner.Scan(LogPath);
        _initializing = true;
        Sessions.Clear();
        Sessions.Add(new SessionInfo { IsAll = true, Label = "★ Alle Sessions (zusammen)" });
        foreach (var s in found) Sessions.Add(s);
        if (selectCurrent)
            SelectedSession = Sessions.FirstOrDefault();   // Default: „Alle Sessions"
        _initializing = false;
    }

    [RelayCommand]
    private void Detect()
    {
        var found = PathFinder.FindBest();
        if (found != null) { LogPath = found; RefreshSessions(selectCurrent: true); Status = "erkannt: " + ChannelOf(found); }
        else Status = "keine Game.log gefunden";
    }

    // Game.log manuell wählen (z.B. wenn SC auf einer anderen Platte liegt).
    [RelayCommand]
    private async Task Browse()
    {
        var tl = UiServices.TopLevel;
        if (tl is null) return;

        IStorageFolder? start = null;
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (dir != null && Directory.Exists(dir))
                start = await tl.StorageProvider.TryGetFolderFromPathAsync(dir);
        }
        catch { /* ignore */ }

        var files = await tl.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Game.log auswählen",
            AllowMultiple = false,
            SuggestedStartLocation = start,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Star Citizen Log") { Patterns = new[] { "Game.log", "*.log" } },
                new FilePickerFileType("Alle Dateien") { Patterns = new[] { "*" } }
            }
        });

        var picked = files.FirstOrDefault()?.TryGetLocalPath();
        if (picked is null) return;

        LogPath = picked;
        RefreshSessions(selectCurrent: true);
        Status = "gewählt: " + picked;
    }

    [RelayCommand]
    private void Toggle()
    {
        if (Running)
        {
            _tailer?.Stop();
            Running = false;
            Status = "gestoppt";
            return;
        }
        LoadSession();
    }

    // Session (neu) laden: Tailer stoppen, alles zurücksetzen, von vorne einlesen.
    void LoadSession()
    {
        _tailer?.Stop();
        Reset();

        if (SelectedSession?.IsAll == true) { LoadAllSessions(); return; }

        _tailer = new LogTailer(LogPath);
        _tailer.Line += OnLine;
        _tailer.Status += s => Dispatcher.UIThread.Post(() => Status = s);
        _tailer.Start(fromStart: true);
        Running = true;
    }

    // Alle Logs (aktuell + Backups) chronologisch zusammen einlesen.
    void LoadAllSessions()
    {
        Running = true;
        Status = "lese alle Sessions…";
        var files = SessionScanner.Scan(LogPath)
            .Where(s => !s.IsAll)
            .OrderBy(s => s.Start)
            .Select(s => s.Path)
            .ToList();

        Task.Run(() =>
        {
            int n = 0;
            foreach (var f in files)
            {
                var parser = new LogParser();   // pro Datei frischer Zustand
                foreach (var line in ReadSharedLines(f))
                {
                    var e = parser.Feed(line);
                    if (e != null) Dispatcher.UIThread.Post(() => Apply(e));
                }
                n++;
            }
            Dispatcher.UIThread.Post(() => Status = $"alle {n} Sessions geladen");
        });
    }

    static System.Collections.Generic.IEnumerable<string> ReadSharedLines(string file)
    {
        FileStream fs;
        try { fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
        catch { yield break; }
        using (fs)
        using (var sr = new StreamReader(fs))
        {
            string? l;
            while ((l = sr.ReadLine()) != null) yield return l;
        }
    }

    void Reset()
    {
        _parser = new LogParser();
        Events.Clear();
        ShipsSeen.Clear();
        _shipSet.Clear();
        IncomeStats.Clear();
        SpendStats.Clear();
        TopTransactions.Clear();
        TotalIn = TotalReward = TotalOut = TotalPurchases = TotalSales = TotalTrade = 0;
        CurrentLocation = CurrentShip = "—";
        LastInventory = "—";
        _sessionStart = _sessionEnd = null;
        _running = StartBalance();
        OnPropertyChanged(nameof(NetBalanceText));
        OnPropertyChanged(nameof(NetSign));
        OnPropertyChanged(nameof(ExpectedText));
        OnPropertyChanged(nameof(ExpectedBalance));
        OnPropertyChanged(nameof(FlowText));
        OnPropertyChanged(nameof(TradeText));
        OnPropertyChanged(nameof(FleetText));
        OnPropertyChanged(nameof(ShipsSeenText));
        OnPropertyChanged(nameof(SessionSpanText));
    }

    [RelayCommand]
    private async Task ExportCsv()
    {
        var path = await PickSaveAsync("sc-transaktionen.csv", "CSV", "csv");
        if (path == null) return;

        var sb = new StringBuilder();
        sb.AppendLine("Zeit;Typ;Betrag;Detail");
        foreach (var e in Events.Reverse()) // chronologisch (Liste ist neueste zuerst)
            sb.Append(e.TimeText).Append(';')
              .Append(e.KindText).Append(';')
              .Append(e.Amount).Append(';')
              .Append(CsvEscape(e.Detail)).Append('\n');

        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(true));
        Status = "CSV gespeichert: " + path;
    }

    [RelayCommand]
    private async Task ExportJson()
    {
        var path = await PickSaveAsync("sc-transaktionen.json", "JSON", "json");
        if (path == null) return;

        var report = new
        {
            system = _parser.Meta,
            session = new
            {
                start = _sessionStart,
                end = _sessionEnd,
                source = LogPath
            },
            totals = new
            {
                einnahmen = IncomeAll,
                ausgaben = SpendAll,
                netto = NetAll,
                transfersIn = TotalIn,
                transfersOut = TotalOut,
                verkaeufeItem = TotalSales,
                handelFracht = TotalTrade,
                kaeufe = TotalPurchases
            },
            standort = CurrentLocation,
            schiffAktuell = CurrentShip,
            flotte = ShipsSeen.ToArray(),
            ausruestung = Events.Where(e => e.Kind == EventKind.Loadout).Select(e => e.Detail).ToArray(),
            events = Events.Reverse().Select(e => new
            {
                time = e.Time,
                kind = e.Kind.ToString(),
                amount = e.Amount,
                detail = e.Detail
            })
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false));
        Status = "JSON gespeichert: " + path;
    }

    void OnLine(string line)
    {
        var e = _parser.Feed(line);
        if (e == null) return;
        Dispatcher.UIThread.Post(() => Apply(e));
    }

    // Ein erkanntes Ereignis verarbeiten (Totals, Saldo, Flotte, Liste). Immer auf UI-Thread.
    void Apply(LogEntry e)
    {
        {
            if (_sessionStart is null || e.Time < _sessionStart) _sessionStart = e.Time;
            if (_sessionEnd is null || e.Time > _sessionEnd) _sessionEnd = e.Time;

            Events.Insert(0, e);
            if (Events.Count > 100000) Events.RemoveAt(Events.Count - 1);

            switch (e.Kind)
            {
                case EventKind.TransferIn:
                    TotalIn += e.Amount;
                    break;
                case EventKind.MissionReward:
                    TotalReward += e.Amount;
                    break;
                case EventKind.TransferOut:
                    TotalOut += -e.Amount;       // Amount ist negativ
                    break;
                case EventKind.Purchase:
                    TotalPurchases += -e.Amount;
                    ResolveItemName(e);
                    break;
                case EventKind.Sale:
                    TotalSales += e.Amount;
                    ResolveItemName(e);
                    break;
                case EventKind.Trade:
                    TotalTrade += e.Amount;
                    break;
                case EventKind.Location:
                    CurrentLocation = e.Detail;
                    break;
                case EventKind.Vehicle:
                    CurrentShip = e.Detail;
                    break;
                case EventKind.Inventory:
                    LastInventory = e.Detail;
                    break;
            }

            // Mitlaufender Kontostand bei Geld-Ereignissen
            if (IsMoney(e.Kind))
            {
                _running += e.Amount;
                e.BalanceAfter = _running;
                e.HasBalance = true;
                RebuildStats();
            }

            // Flotte: Schiffe aus Vehicle- UND Quantum-Events sammeln
            if (!string.IsNullOrEmpty(e.Ship) && _shipSet.Add(e.Ship))
            {
                ShipsSeen.Add(e.Ship);
                OnPropertyChanged(nameof(FleetText));
                OnPropertyChanged(nameof(ShipsSeenText));
            }

            OnPropertyChanged(nameof(NetBalanceText));
            OnPropertyChanged(nameof(NetSign));
            OnPropertyChanged(nameof(FlowText));
            OnPropertyChanged(nameof(TradeText));
            OnPropertyChanged(nameof(ExpectedText));
            OnPropertyChanged(nameof(ExpectedBalance));
            OnPropertyChanged(nameof(SessionSpanText));
            OnPropertyChanged(nameof(MetaSummary));
        }
    }

    // Item-Namen live über UEX nachladen und den Eintrag aktualisieren.
    async void ResolveItemName(LogEntry e)
    {
        var name = await ItemNames.ResolveAsync(e.ItemRef);
        if (name is null) return;
        Dispatcher.UIThread.Post(() => e.Detail = $"{name}  {e.Suffix}");
    }

    static async Task<string?> PickSaveAsync(string suggested, string typeName, string ext)
    {
        var tl = UiServices.TopLevel;
        if (tl is null) return null;

        var file = await tl.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggested,
            DefaultExtension = ext,
            FileTypeChoices = new[]
            {
                new FilePickerFileType(typeName) { Patterns = new[] { "*." + ext } }
            }
        });

        return file?.TryGetLocalPath();
    }

    static string CsvEscape(string s) =>
        s.Contains(';') || s.Contains('"') || s.Contains('\n')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;

    static string ChannelOf(string path)
    {
        var dir = Path.GetDirectoryName(path);
        return dir is null ? path : Path.GetFileName(dir);
    }
}
