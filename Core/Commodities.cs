using System;
using System.Collections.Generic;

namespace SCLogReader.Core;

/// <summary>
/// Mappt resourceGUID (Fracht/Waren-Verkäufe) → Warenname.
/// UEX/Items kennen diese GUIDs nicht per uuid, daher lokale Tabelle
/// (per Internet-Recherche befüllbar). Unbekannt → "Fracht".
/// </summary>
public static partial class Commodities
{
    // Map kommt aus CommoditiesData.cs (auto-generiert aus scunpacked, 750+ Waren).

    public static string Resolve(string? guid)
    {
        if (guid != null && Map.TryGetValue(guid, out var name)) return name;
        // unbekannt: Kurz-GUID zeigen, damit man sie leicht nachtragen kann
        var hint = string.IsNullOrEmpty(guid) ? "" : $" [{guid[..Math.Min(8, guid.Length)]}]";
        return $"Fracht{hint}";
    }
}
