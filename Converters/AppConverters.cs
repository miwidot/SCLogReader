using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SCLogReader.Models;

namespace SCLogReader.Converters;

/// <summary>Färbt Beträge: grün = rein, rot = raus, gedimmt = neutral.</summary>
public class AmountToBrushConverter : IValueConverter
{
    public static readonly AmountToBrushConverter Instance = new();

    static readonly IBrush In = new SolidColorBrush(Color.Parse("#4ADE80"));
    static readonly IBrush Out = new SolidColorBrush(Color.Parse("#F87171"));
    static readonly IBrush Neutral = new SolidColorBrush(Color.Parse("#8B949E"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        long amt = value is long l ? l : 0;
        return amt > 0 ? In : amt < 0 ? Out : Neutral;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Hintergrund eines Filter-Chips: aktiv = Akzent, sonst gedimmt.</summary>
public class FilterActiveConverter : IValueConverter
{
    public static readonly FilterActiveConverter Instance = new();

    static readonly IBrush Active = new SolidColorBrush(Color.Parse("#1F6FEB"));
    static readonly IBrush Idle = new SolidColorBrush(Color.Parse("#21262D"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value as string, parameter as string, StringComparison.Ordinal) ? Active : Idle;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Akzentfarbe je Event-Typ (für den Icon-Punkt).</summary>
public class KindToBrushConverter : IValueConverter
{
    public static readonly KindToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var kind = value is EventKind k ? k : EventKind.Info;
        var hex = kind switch
        {
            EventKind.TransferIn => "#4ADE80",
            EventKind.TransferOut => "#F87171",
            EventKind.MissionReward => "#FBBF24",
            EventKind.Sale => "#4ADE80",
            EventKind.Trade => "#22D3EE",
            EventKind.Location => "#38BDF8",
            EventKind.Inventory => "#A78BFA",
            EventKind.Vehicle => "#FB923C",
            EventKind.Quantum => "#22D3EE",
            EventKind.Mission => "#FBBF24",
            EventKind.Jurisdiction => "#F472B6",
            EventKind.Party => "#A78BFA",
            EventKind.MedBed => "#F87171",
            EventKind.Hangar => "#38BDF8",
            EventKind.Loadout => "#94A3B8",
            EventKind.Offer => "#FBBF24",
            EventKind.ShipLoss => "#F87171",
            EventKind.Death => "#EF4444",
            EventKind.Impound => "#FB923C",
            EventKind.Friend => "#4ADE80",
            EventKind.Entitlement => "#94A3B8",
            EventKind.Blueprint => "#C084FC",
            EventKind.Gear => "#FB7185",
            EventKind.Kill => "#EF4444",
            _ => "#8B949E"
        };
        return new SolidColorBrush(Color.Parse(hex));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
