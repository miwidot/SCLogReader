using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SCLogReader.Core;

/// <summary>
/// Löst itemClassGUID → lesbarer Item-Name über die UEX-API auf
/// (https://api.uexcorp.uk/2.0/items?uuid=...). Ergebnisse werden gecacht;
/// ohne Netz/Treffer greift ein lokaler Fallback (Code lesbar machen).
/// </summary>
public static class ItemNames
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    static readonly ConcurrentDictionary<string, string> Cache = new();

    /// <summary>Liefert den UEX-Namen oder null (dann Fallback verwenden).</summary>
    public static async Task<string?> ResolveAsync(string? guid)
    {
        if (string.IsNullOrWhiteSpace(guid)) return null;
        if (Cache.TryGetValue(guid, out var cached))
            return cached.Length == 0 ? null : cached;

        try
        {
            var json = await Http.GetStringAsync($"https://api.uexcorp.uk/2.0/items?uuid={guid}");
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
            {
                var it = data[0];
                var name = it.TryGetProperty("name", out var n) ? n.GetString() : null;
                var company = it.TryGetProperty("company_name", out var c) ? c.GetString() : null;

                if (!string.IsNullOrEmpty(name))
                {
                    var result = string.IsNullOrEmpty(company) ? name! : $"{name} · {company}";
                    Cache[guid] = result;
                    return result;
                }
            }
            Cache[guid] = "";   // negativ cachen, nicht erneut anfragen
        }
        catch
        {
            // offline/timeout -> kein Cache, beim nächsten Mal neu versuchen
        }
        return null;
    }

    /// <summary>Macht aus "KLWE_LaserRepeater_S4" -> "KLWE LaserRepeater S4".</summary>
    public static string CleanFallback(string raw) =>
        string.IsNullOrWhiteSpace(raw) ? raw : raw.Replace('_', ' ').Trim();
}
