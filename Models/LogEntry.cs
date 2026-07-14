using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SCLogReader.Models;

public enum EventKind
{
    TransferIn,
    TransferOut,
    MissionReward,
    Purchase,
    Sale,
    Trade,
    Location,
    Inventory,
    Vehicle,
    Quantum,
    Mission,
    Jurisdiction,
    Party,
    MedBed,
    Hangar,
    Loadout,
    Offer,
    ShipLoss,
    Death,
    Impound,
    Friend,
    Entitlement,
    Blueprint,
    Gear,
    Kill,
    MissionDone,
    Fine,
    Crime,
    Refinery,
    Injury,
    Loot,
    Info
}

public partial class LogEntry : ObservableObject
{
    public DateTime Time { get; init; }
    public EventKind Kind { get; init; }

    /// <summary>aUEC, signed: positive = rein, negative = raus, 0 = kein Geldwert.</summary>
    public long Amount { get; init; }

    /// <summary>itemClassGUID (nur bei Käufen) für die Namensauflösung über UEX.</summary>
    public string? ItemRef { get; init; }

    /// <summary>Zusatz hinter dem Namen (z.B. "×1 · Cargo Office"), bleibt bei Namensupdate erhalten.</summary>
    public string? Suffix { get; init; }

    /// <summary>Schiffsname (bei Vehicle/Quantum) für die Flotten-Liste.</summary>
    public string? Ship { get; init; }

    /// <summary>Anzeigetext – wird bei Käufen asynchron mit dem echten Item-Namen ersetzt.</summary>
    [ObservableProperty] private string detail = "";

    public string TimeText => Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string KindText => Kind switch
    {
        EventKind.TransferIn => "Eingang",
        EventKind.TransferOut => "Ausgang",
        EventKind.MissionReward => "Belohnung",
        EventKind.Purchase => "Kauf",
        EventKind.Sale => "Verkauf",
        EventKind.Trade => "Handel",
        EventKind.Location => "Standort",
        EventKind.Inventory => "Lager",
        EventKind.Vehicle => "Schiff",
        EventKind.Quantum => "Quantum",
        EventKind.Mission => "Mission",
        EventKind.Jurisdiction => "Gebiet",
        EventKind.Party => "Party",
        EventKind.MedBed => "Med-Bett",
        EventKind.Hangar => "Hangar",
        EventKind.Loadout => "Ausrüstung",
        EventKind.Offer => "Angebot",
        EventKind.ShipLoss => "Verlust",
        EventKind.Death => "Tod",
        EventKind.Impound => "Beschlagn.",
        EventKind.Friend => "Freund",
        EventKind.Entitlement => "Miete",
        EventKind.Blueprint => "Bauplan",
        EventKind.Gear => "Defekt",
        EventKind.Kill => "Kampf",
        EventKind.MissionDone => "Auftrag ✓",
        EventKind.Fine => "Strafe",
        EventKind.Crime => "Straftat",
        EventKind.Refinery => "Veredelung",
        EventKind.Injury => "Verletzung",
        EventKind.Loot => "Loot",
        _ => "Info"
    };

    public string AmountText => Amount != 0 ? $"{Amount:N0}" : "";

    // Farbe der DETAIL-Zelle: Missions-Status farbig (grün=fertig, rot=fehlgeschlagen,
    // blau=angenommen/neu, gelb=zurückgezogen), sonst Standard.
    static readonly IBrush _detailDefault = new SolidColorBrush(Color.Parse("#E6EDF3"));
    static readonly IBrush _missGreen = new SolidColorBrush(Color.Parse("#3FB950"));
    static readonly IBrush _missRed = new SolidColorBrush(Color.Parse("#F85149"));
    static readonly IBrush _missBlue = new SolidColorBrush(Color.Parse("#58A6FF"));
    static readonly IBrush _missAmber = new SolidColorBrush(Color.Parse("#D29922"));

    public IBrush StatusBrush
    {
        get
        {
            if (Kind == EventKind.MissionDone) return _missGreen;
            if (Kind != EventKind.Mission) return _detailDefault;
            var d = Detail ?? "";
            if (d.StartsWith("Auftrag abgeschlossen") || d.StartsWith("Contract Complete")) return _missGreen;
            if (d.StartsWith("Auftrag fehlgeschlagen") || d.StartsWith("Contract Failed")) return _missRed;
            if (d.StartsWith("Auftrag zurückgezogen")) return _missAmber;
            return _missBlue;   // angenommen / Neuer Auftrag / New Objective
        }
    }

    // Mitlaufender Kontostand nach diesem Ereignis (nur Geld-Events)
    [ObservableProperty] private long balanceAfter;
    [ObservableProperty] private bool hasBalance;
    public string BalanceAfterText => HasBalance ? $"{BalanceAfter:N0}" : "";
    partial void OnBalanceAfterChanged(long value) => OnPropertyChanged(nameof(BalanceAfterText));
    partial void OnHasBalanceChanged(bool value) => OnPropertyChanged(nameof(BalanceAfterText));

    public string Icon => Kind switch
    {
        EventKind.TransferIn => "▼",
        EventKind.TransferOut => "▲",
        EventKind.MissionReward => "★",
        EventKind.Purchase => "↧",
        EventKind.Sale => "↥",
        EventKind.Trade => "⇄",
        EventKind.Location => "◉",
        EventKind.Inventory => "▣",
        EventKind.Vehicle => "✈",
        EventKind.Quantum => "✦",
        EventKind.Mission => "✓",
        EventKind.Jurisdiction => "⬢",
        EventKind.Party => "♟",
        EventKind.MedBed => "✚",
        EventKind.Hangar => "⌂",
        EventKind.Loadout => "⛨",
        EventKind.Offer => "◇",
        EventKind.ShipLoss => "✸",
        EventKind.Death => "☠",
        EventKind.Impound => "⊠",
        EventKind.Friend => "♥",
        EventKind.Entitlement => "⧉",
        EventKind.Blueprint => "⬡",
        EventKind.Gear => "✖",
        EventKind.Kill => "⚔",
        EventKind.MissionDone => "✔",
        EventKind.Fine => "⚖",
        EventKind.Crime => "⚠",
        EventKind.Refinery => "⚗",
        EventKind.Injury => "⚕",
        EventKind.Loot => "◈",
        _ => "·"
    };
}
