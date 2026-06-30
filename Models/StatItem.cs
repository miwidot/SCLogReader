using System;
using Avalonia.Media;

namespace SCLogReader.Models;

/// <summary>Ein Balken/Posten in der Geld-Statistik (Label, Wert, Balkenbreite, Farbe, Zeit).</summary>
public class StatItem
{
    public string Label { get; init; } = "";
    public string Sub { get; init; } = "";
    public long Value { get; init; }
    public double BarWidth { get; init; }
    public IBrush Color { get; init; } = Brushes.Gray;
    public DateTime Time { get; init; }

    public string ValueText => $"{Value:N0} aUEC";
    public string WhenText => Time == default ? "" : Time.ToLocalTime().ToString("dd.MM. HH:mm");
}
