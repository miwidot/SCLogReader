using System;
using System.IO;

namespace SCLogReader.Core;

/// <summary>
/// Schreibt eine Debug-Datei NEBEN die .exe (SCLogReader.debug.log).
/// Zweck: bei fremden Logs sehen, was schiefläuft und welche Events wir
/// noch nicht abdecken (unbekannte Notifications etc.).
/// </summary>
public static class Logger
{
    static readonly object Lock = new();
    static readonly string Path;

    static Logger()
    {
        string dir;
        try { dir = System.IO.Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory) ?? "."; }
        catch { dir = "."; }
        Path = System.IO.Path.Combine(dir, "SCLogReader.debug.log");

        try
        {
            // pro Start frische Datei (außer sie ist riesig -> sowieso neu)
            File.WriteAllText(Path,
                $"=== SC Log Reader {Updater.CurrentVersion} · Start {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
        }
        catch { /* nicht schreibbar -> egal */ }
    }

    public static void Log(string msg)
    {
        try { lock (Lock) File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss}  {msg}{Environment.NewLine}"); }
        catch { /* ignore */ }
    }

    public static void Error(string context, Exception ex) => Log($"[ERROR] {context}: {ex.GetType().Name}: {ex.Message}");
}
