using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SCLogReader.Core;

public class SessionInfo
{
    public string Path { get; init; } = "";
    public string Label { get; init; } = "";
    public DateTime Start { get; init; }
    public bool IsCurrent { get; init; }
    public bool IsAll { get; init; }

    public override string ToString() => Label;   // ComboBox-Anzeige
}

/// <summary>
/// Findet alle Sessions: die aktuelle Game.log + alle logbackups daneben.
/// Startzeit wird aus dem Datei-Header gelesen (erster Zeitstempel).
/// </summary>
public static class SessionScanner
{
    static readonly Regex Ts = new(@"<(?<ts>\d{4}-\d{2}-\d{2}T[\d:.]+Z)>", RegexOptions.Compiled);

    public static List<SessionInfo> Scan(string gameLogPath)
    {
        var list = new List<SessionInfo>();
        var dir = Path.GetDirectoryName(gameLogPath) ?? ".";

        if (File.Exists(gameLogPath))
        {
            var st = ReadStart(gameLogPath);
            list.Add(new SessionInfo
            {
                Path = gameLogPath,
                Start = st,
                IsCurrent = true,
                Label = $"{st.ToLocalTime():dd.MM. HH:mm}  ·  aktuell"
            });
        }

        var backups = Path.Combine(dir, "logbackups");
        if (Directory.Exists(backups))
        {
            foreach (var f in Directory.GetFiles(backups, "*.log"))
            {
                var st = ReadStart(f);
                list.Add(new SessionInfo
                {
                    Path = f,
                    Start = st,
                    Label = $"{st.ToLocalTime():dd.MM. HH:mm}"
                });
            }
        }

        return list.OrderByDescending(s => s.Start).ToList();
    }

    static DateTime ReadStart(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            for (int i = 0; i < 3; i++)
            {
                var line = sr.ReadLine();
                if (line == null) break;
                var m = Ts.Match(line);
                if (m.Success && DateTime.TryParse(m.Groups["ts"].Value, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
                    return dt;
            }
        }
        catch { /* ignore */ }

        try { return File.GetLastWriteTimeUtc(path); } catch { return DateTime.MinValue; }
    }
}
