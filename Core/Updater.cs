using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace SCLogReader.Core;

/// <summary>
/// Auto-Updater über GitHub Releases: prüft die neueste Version,
/// lädt die .exe und ersetzt sich selbst (über ein kleines Helfer-Batch).
/// </summary>
public static class Updater
{
    const string Repo = "miwidot/SCLogReader";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public record Info(string Version, string Url);

    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    /// <summary>Liefert Update-Info, falls eine neuere Version vorliegt; sonst null.</summary>
    public static async Task<Info?> CheckAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{Repo}/releases/latest");
            req.Headers.UserAgent.ParseAdd("SCLogReader");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var tag = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');

            string? url = null;
            if (doc.RootElement.TryGetProperty("assets", out var assets))
                foreach (var a in assets.EnumerateArray())
                    if ((a.GetProperty("name").GetString() ?? "").EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        url = a.GetProperty("browser_download_url").GetString();
                        break;
                    }

            if (tag != null && url != null && IsNewer(tag, CurrentVersion))
                return new Info(tag, url);
        }
        catch { /* offline / kein Release -> kein Update */ }
        return null;
    }

    /// <summary>Lädt das Update und startet das Ersetzen (App muss danach beenden).</summary>
    public static async Task ApplyAsync(Info info)
    {
        var cur = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;
        var tmp = cur + ".new";

        var bytes = await Http.GetByteArrayAsync(info.Url);
        await File.WriteAllBytesAsync(tmp, bytes);

        // Batch: warten bis App zu ist, alte exe ersetzen, neu starten, sich selbst löschen.
        var bat = Path.Combine(Path.GetTempPath(), "sclr_update.bat");
        await File.WriteAllTextAsync(bat,
            "@echo off\r\n" +
            "timeout /t 2 /nobreak >nul\r\n" +
            $"move /y \"{tmp}\" \"{cur}\" >nul\r\n" +
            $"start \"\" \"{cur}\"\r\n" +
            "del \"%~f0\"\r\n");

        Process.Start(new ProcessStartInfo
        {
            FileName = bat,
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    static bool IsNewer(string remote, string local)
    {
        static (int, int, int) P(string s)
        {
            var p = s.Split('.', '-', '+').Where(x => int.TryParse(x, out _)).Select(int.Parse).ToArray();
            return (p.ElementAtOrDefault(0), p.ElementAtOrDefault(1), p.ElementAtOrDefault(2));
        }
        return P(remote).CompareTo(P(local)) > 0;
    }
}
