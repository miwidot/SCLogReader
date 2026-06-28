using Avalonia.Controls;

namespace SCLogReader.Services;

/// <summary>Hält die TopLevel-Referenz, damit das ViewModel Datei-Dialoge öffnen kann.</summary>
public static class UiServices
{
    public static TopLevel? TopLevel { get; set; }
}
