using System;
using System.IO;
using System.Text.Json;

namespace SCLogReader.Core;

public class AppSettings
{
    public string? LogPath { get; set; }
    public long Balance { get; set; }   // echter Kontostand (manuell eingetragen)

    /// <summary>Zeitpunkt des Kontostand-Eintrags. Nur Bewegungen DANACH werden angerechnet —
    /// der eingetragene Wert ist der Stand von JETZT, nicht der Startwert der ganzen Historie
    /// (die ist ohnehin lückenhaft, weil SC alte Logs löscht).</summary>
    public DateTime? BalanceSetAt { get; set; }
}

/// <summary>Merkt sich Einstellungen (Log-Pfad, Kontostand) über Starts hinweg.</summary>
public static class Settings
{
    static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCLogReader");

    static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* defekte Datei ignorieren */ }
        return new AppSettings();
    }

    public static void Save(AppSettings s)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* nicht schreibbar -> egal */ }
    }
}
