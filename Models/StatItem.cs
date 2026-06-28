using Avalonia.Media;

namespace SCLogReader.Models;

/// <summary>Ein Balken in der Geld-Statistik (Label, Wert, Balkenbreite, Farbe).</summary>
public class StatItem
{
    public string Label { get; init; } = "";
    public long Value { get; init; }
    public double BarWidth { get; init; }
    public IBrush Color { get; init; } = Brushes.Gray;

    public string ValueText => $"{Value:N0} aUEC";
}
