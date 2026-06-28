using System;
using System.Collections.Generic;

namespace SCLogReader.Core;

/// <summary>
/// Mappt resourceGUID (Fracht/Waren-Verkäufe) → Warenname.
/// UEX/Items kennen diese GUIDs nicht per uuid, daher lokale Tabelle
/// (per Internet-Recherche befüllbar). Unbekannt → "Fracht".
/// </summary>
public static class Commodities
{
    static readonly Dictionary<string, string> Map = new()
    {
        ["1b4c4042-5fdc-4b52-bec4-07085cb3520a"] = "Tin",
    };

    public static string Resolve(string? guid)
    {
        if (guid != null && Map.TryGetValue(guid, out var name)) return name;
        // unbekannt: Kurz-GUID zeigen, damit man sie leicht nachtragen kann
        var hint = string.IsNullOrEmpty(guid) ? "" : $" [{guid[..Math.Min(8, guid.Length)]}]";
        return $"Fracht{hint}";
    }
}
