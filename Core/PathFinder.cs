using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SCLogReader.Core;

/// <summary>
/// Findet die Game.log automatisch über alle festen Laufwerke und die
/// üblichen Installationswurzeln + Channels (LIVE/PTU/EPTU/…).
/// </summary>
public static class PathFinder
{
    static readonly string[] Channels = { "LIVE", "PTU", "EPTU", "HOTFIX", "TECH-PREVIEW" };

    public static IEnumerable<string> FindAll()
    {
        var roots = new List<string>();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady &&
                     d.DriveType is DriveType.Fixed or DriveType.Removable or DriveType.Network))
        {
            var r = drive.RootDirectory.FullName;
            roots.Add(Path.Combine(r, "Program Files", "Roberts Space Industries", "StarCitizen"));
            roots.Add(Path.Combine(r, "Roberts Space Industries", "StarCitizen"));
            roots.Add(Path.Combine(r, "Games", "Roberts Space Industries", "StarCitizen"));
            roots.Add(Path.Combine(r, "StarCitizen"));
        }

        foreach (var root in roots.Distinct())
        {
            if (!Directory.Exists(root)) continue;
            foreach (var ch in Channels)
            {
                var p = Path.Combine(root, ch, "Game.log");
                if (File.Exists(p)) yield return p;
            }
        }
    }

    /// <summary>Neueste Game.log (zuletzt geändert), oder null.</summary>
    public static string? FindBest()
    {
        try
        {
            return FindAll()
                .Select(p => new FileInfo(p))
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .Select(fi => fi.FullName)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }
}
