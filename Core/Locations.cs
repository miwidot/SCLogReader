using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SCLogReader.Core;

/// <summary>
/// Übersetzt die internen Location-Codes (z.B. RR_HUR_L4) in lesbare
/// Station-Namen. Bekannte Codes per Tabelle, der Rest über Heuristiken
/// (Jumppunkte, Lagrange, LEO) bzw. als aufgehübschter Fallback.
/// </summary>
public static class Locations
{
    static readonly Dictionary<string, string> Map = new()
    {
        // Hurston Lagrange (Rest Stops)
        ["RR_HUR_L1"] = "HUR-L1 Green Glade Station",
        ["RR_HUR_L2"] = "HUR-L2 Faithful Dream Station",
        ["RR_HUR_L3"] = "HUR-L3 Thundering Express Station",
        ["RR_HUR_L4"] = "HUR-L4 Melodic Fields Station",
        ["RR_HUR_L5"] = "HUR-L5 High Course Station",
        // ArcCorp Lagrange
        ["RR_ARC_L1"] = "ARC-L1 Wide Forest Station",
        ["RR_ARC_L2"] = "ARC-L2 Lively Pathway Station",
        ["RR_ARC_L3"] = "ARC-L3 Modern Express Station",
        ["RR_ARC_L4"] = "ARC-L4 Faint Glen Station",
        ["RR_ARC_L5"] = "ARC-L5 Yellow Jewel Station",
        // Crusader Lagrange
        ["RR_CRU_L1"] = "CRU-L1 Ambitious Dream Station",
        ["RR_CRU_L4"] = "CRU-L4 Shallow Fields Station",
        ["RR_CRU_L5"] = "CRU-L5 Beautiful Glen Station",
        // microTech Lagrange
        ["RR_MIC_L1"] = "MIC-L1 Shallow Frontier Station",
        ["RR_MIC_L2"] = "MIC-L2 Long Forest Station",
        ["RR_MIC_L3"] = "MIC-L3 Endless Odyssey Station",
        ["RR_MIC_L4"] = "MIC-L4 Red Festival Station",
        ["RR_MIC_L5"] = "MIC-L5 Modern Icarus Station",
        // Orbital-Stationen (LEO)
        ["RR_HUR_LEO"] = "Everus Harbor · Hurston",
        ["RR_ARC_LEO"] = "Baijini Point · ArcCorp",
        ["RR_MIC_LEO"] = "Port Tressler · microTech",
        ["RR_CRU_LEO"] = "Seraphim Station · Crusader",
        // Planeten / Städte
        ["Stanton1_Lorville"] = "Lorville · Hurston",
        ["Stanton2_Orison"] = "Orison · Crusader",
        ["Stanton1_HurdynMining_HDMSOparei"] = "HDMS-Oparei · Hurston",
        ["Stanton1_DistributionCentre_Hurston_Cassillo"] = "Verteilzentrum Cassillo · Hurston",
        ["Stanton2c_RayariHydro_DeakinsResearch"] = "Rayari Deakins Research · Yela",
        ["Stanton4a_RayariHydro_Anvik"] = "Rayari Anvik Research · Calliope",
        ["Stanton4a_RayariHydro_Kaltag"] = "Rayari Kaltag Research · Calliope",
        ["AsteroidClusterBase_Nyx_Social_Keeger_002"] = "Asteroidenbasis Keeger · Nyx",
    };

    static readonly Regex Lagrange = new(@"^RR_(?<sys>[A-Z]{3})_L(?<n>\d)$", RegexOptions.Compiled);
    static readonly Regex Jump = new(@"^RR_JP_(?<route>[A-Za-z]+)$", RegexOptions.Compiled);
    static readonly Regex Pyro = new(@"^RR_P(?<p>\d)_L(?<n>\d)$", RegexOptions.Compiled);

    public static string Resolve(string code)
    {
        if (Map.TryGetValue(code, out var name)) return name;

        var jp = Jump.Match(code);
        if (jp.Success)
            return SplitCamel(jp.Groups["route"].Value) + " · Jumppunkt-Station";

        var py = Pyro.Match(code);
        if (py.Success)
            return $"Pyro {py.Groups["p"].Value} · L{py.Groups["n"].Value} Rest Stop";

        var lg = Lagrange.Match(code);
        if (lg.Success)
            return $"{lg.Groups["sys"].Value}-L{lg.Groups["n"].Value} Station";

        // Fallback: Code lesbar machen statt roh anzeigen
        return Prettify(code);
    }

    static string SplitCamel(string s) =>
        Regex.Replace(s, "(?<=[a-z])(?=[A-Z])", " ⇄ ");

    static string Prettify(string code)
    {
        var s = code.Replace('_', ' ').Trim();
        return s.Length == 0 ? code : s;
    }
}
