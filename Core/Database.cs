using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using SCLogReader.Models;

namespace SCLogReader.Core;

/// <summary>
/// Lokale SQLite-Datenbank als nachbaubarer Cache/Index der fertigen Sessions.
/// Quelle der Wahrheit bleiben die archivierten Roh-Logs (LogArchive).
/// - Schema-Version: PRAGMA user_version (Tabellenstruktur)
/// - Parser-Version: bei Erhöhung wird die DB aus dem Archiv NEU aufgebaut.
/// </summary>
public static class Database
{
    const int SchemaVersion = 1;
    const int ParserVersion = 3;   // erhöhen, wenn der Parser neue Felder/Events liefert

    static string DbPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCLogReader", "sessions.db");

    static string Conn => $"Data Source={DbPath}";

    public static void Init()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        using var db = new SqliteConnection(Conn);
        db.Open();

        Exec(db, @"CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT);
                   CREATE TABLE IF NOT EXISTS sessions(name TEXT PRIMARY KEY, start TEXT, end TEXT);
                   CREATE TABLE IF NOT EXISTS events(session TEXT, time TEXT, kind TEXT, amount INTEGER, detail TEXT, ship TEXT);
                   CREATE INDEX IF NOT EXISTS ix_events_session ON events(session);");

        // Parser-Version prüfen -> bei Änderung Cache leeren (wird neu indexiert)
        var stored = GetMeta(db, "parserVersion");
        if (stored != ParserVersion.ToString(CultureInfo.InvariantCulture))
        {
            Exec(db, "DELETE FROM events; DELETE FROM sessions;");
            SetMeta(db, "parserVersion", ParserVersion.ToString(CultureInfo.InvariantCulture));
            Logger.Log($"DB: Parser-Version geändert -> Cache geleert (rebuild).");
        }
        SetMeta(db, "schemaVersion", SchemaVersion.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Parst und speichert alle Logs, die noch nicht in der DB sind. Liefert Anzahl neuer.</summary>
    public static int IndexNew(IEnumerable<string> logFiles)
    {
        using var db = new SqliteConnection(Conn);
        db.Open();
        int added = 0;

        foreach (var file in logFiles)
        {
            var name = Path.GetFileName(file);
            if (Scalar(db, "SELECT 1 FROM sessions WHERE name=$n", ("$n", name)) != null) continue;

            try
            {
                var parser = new LogParser();
                DateTime? first = null, last = null;
                using var tx = db.BeginTransaction();
                using var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO events(session,time,kind,amount,detail,ship) VALUES($s,$t,$k,$a,$d,$sh)";
                var ps = cmd.Parameters.Add("$s", SqliteType.Text); ps.Value = name;
                var pt = cmd.Parameters.Add("$t", SqliteType.Text);
                var pk = cmd.Parameters.Add("$k", SqliteType.Text);
                var pa = cmd.Parameters.Add("$a", SqliteType.Integer);
                var pd = cmd.Parameters.Add("$d", SqliteType.Text);
                var psh = cmd.Parameters.Add("$sh", SqliteType.Text);

                foreach (var line in ReadShared(file))
                {
                    var e = parser.Feed(line);
                    if (e == null) continue;
                    if (first == null || e.Time < first) first = e.Time;
                    if (last == null || e.Time > last) last = e.Time;
                    pt.Value = e.Time.ToString("o", CultureInfo.InvariantCulture);
                    pk.Value = e.Kind.ToString();
                    pa.Value = e.Amount;
                    pd.Value = e.Detail ?? "";
                    psh.Value = (object?)e.Ship ?? DBNull.Value;
                    cmd.ExecuteNonQuery();
                }

                using (var s = db.CreateCommand())
                {
                    s.Transaction = tx;
                    s.CommandText = "INSERT OR REPLACE INTO sessions(name,start,end) VALUES($n,$st,$en)";
                    s.Parameters.AddWithValue("$n", name);
                    s.Parameters.AddWithValue("$st", (object?)first?.ToString("o", CultureInfo.InvariantCulture) ?? DBNull.Value);
                    s.Parameters.AddWithValue("$en", (object?)last?.ToString("o", CultureInfo.InvariantCulture) ?? DBNull.Value);
                    s.ExecuteNonQuery();
                }
                tx.Commit();
                added++;
            }
            catch (Exception ex) { Logger.Error("Index " + name, ex); }
        }
        return added;
    }

    /// <summary>Alle gespeicherten Events chronologisch (älteste zuerst).</summary>
    public static IEnumerable<LogEntry> LoadAllEvents()
    {
        using var db = new SqliteConnection(Conn);
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT time,kind,amount,detail,ship FROM events ORDER BY time";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            DateTime.TryParse(r.GetString(0), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t);
            Enum.TryParse<EventKind>(r.GetString(1), out var kind);
            yield return new LogEntry
            {
                Time = t,
                Kind = kind,
                Amount = r.GetInt64(2),
                Detail = r.IsDBNull(3) ? "" : r.GetString(3),
                Ship = r.IsDBNull(4) ? null : r.GetString(4)
            };
        }
    }

    /// <summary>Alle Fracht-Verkäufe (Kind=Trade) für die „Handel je Ware"-Übersicht.</summary>
    public static List<LogEntry> AllTrades()
    {
        var list = new List<LogEntry>();
        using var db = new SqliteConnection(Conn);
        db.Open();
        using var c = db.CreateCommand();
        c.CommandText = "SELECT time,amount,detail FROM events WHERE kind='Trade'";
        using var r = c.ExecuteReader();
        while (r.Read())
        {
            DateTime.TryParse(r.GetString(0), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t);
            list.Add(new LogEntry { Time = t, Kind = EventKind.Trade, Amount = r.GetInt64(1), Detail = r.IsDBNull(2) ? "" : r.GetString(2) });
        }
        return list;
    }

    /// <summary>Eindeutige erhaltene Baupläne über alle Sessions.</summary>
    public static List<string> DistinctBlueprints()
    {
        var list = new List<string>();
        using var db = new SqliteConnection(Conn);
        db.Open();
        using var c = db.CreateCommand();
        c.CommandText = "SELECT DISTINCT detail FROM events WHERE kind='Blueprint' ORDER BY detail";
        using var r = c.ExecuteReader();
        while (r.Read()) if (!r.IsDBNull(0)) list.Add(r.GetString(0));
        return list;
    }

    public static int SessionCount()
    {
        using var db = new SqliteConnection(Conn);
        db.Open();
        return Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM sessions") ?? 0);
    }

    public class Agg
    {
        public long In, Reward, Out, Purchases, Sales, Trade;
        public DateTime? Start, End;
        public int Sessions;
        public int MissionsDone;
        public List<string> Ships = new();
    }

    /// <summary>Summen per SQL (kein Vollladen der Events).</summary>
    public static Agg Aggregate()
    {
        var a = new Agg();
        using var db = new SqliteConnection(Conn);
        db.Open();

        using (var c = db.CreateCommand())
        {
            c.CommandText = "SELECT kind, COALESCE(SUM(amount),0) FROM events GROUP BY kind";
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                var kind = r.GetString(0);
                var sum = r.GetInt64(1);
                switch (kind)
                {
                    case "TransferIn": a.In = sum; break;
                    case "MissionReward": a.Reward = sum; break;
                    case "Sale": a.Sales = sum; break;
                    case "Trade": a.Trade = sum; break;
                    case "TransferOut": a.Out = -sum; break;   // Beträge negativ -> positiv
                    case "Purchase": a.Purchases = -sum; break;
                }
            }
        }

        a.Sessions = Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM sessions") ?? 0);
        a.MissionsDone = Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM events WHERE kind='MissionDone'") ?? 0);
        if (Scalar(db, "SELECT MIN(time) FROM events") is string mn && DateTime.TryParse(mn, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var s)) a.Start = s;
        if (Scalar(db, "SELECT MAX(time) FROM events") is string mx && DateTime.TryParse(mx, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var e)) a.End = e;

        using (var c = db.CreateCommand())
        {
            c.CommandText = "SELECT DISTINCT ship FROM events WHERE ship IS NOT NULL ORDER BY ship";
            using var r = c.ExecuteReader();
            while (r.Read()) a.Ships.Add(r.GetString(0));
        }
        return a;
    }

    /// <summary>Größte Geld-Posten per SQL.</summary>
    public static List<LogEntry> TopMoney(int n)
    {
        var list = new List<LogEntry>();
        using var db = new SqliteConnection(Conn);
        db.Open();
        using var c = db.CreateCommand();
        c.CommandText = @"SELECT kind,amount,detail FROM events
                          WHERE kind IN ('TransferIn','TransferOut','MissionReward','Purchase','Sale','Trade')
                          ORDER BY ABS(amount) DESC LIMIT $n";
        c.Parameters.AddWithValue("$n", n);
        using var r = c.ExecuteReader();
        while (r.Read())
        {
            Enum.TryParse<EventKind>(r.GetString(0), out var kind);
            list.Add(new LogEntry { Kind = kind, Amount = r.GetInt64(1), Detail = r.IsDBNull(2) ? "" : r.GetString(2) });
        }
        return list;
    }

    /// <summary>Neueste N Events (für die Tabelle), chronologisch.</summary>
    public static List<LogEntry> RecentEvents(int n)
    {
        var list = new List<LogEntry>();
        using var db = new SqliteConnection(Conn);
        db.Open();
        using var c = db.CreateCommand();
        c.CommandText = "SELECT time,kind,amount,detail,ship FROM events ORDER BY time DESC LIMIT $n";
        c.Parameters.AddWithValue("$n", n);
        using var r = c.ExecuteReader();
        while (r.Read())
        {
            DateTime.TryParse(r.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t);
            Enum.TryParse<EventKind>(r.GetString(1), out var kind);
            list.Add(new LogEntry { Time = t, Kind = kind, Amount = r.GetInt64(2), Detail = r.IsDBNull(3) ? "" : r.GetString(3), Ship = r.IsDBNull(4) ? null : r.GetString(4) });
        }
        list.Reverse();
        return list;
    }

    // ---- Helfer ----
    static IEnumerable<string> ReadShared(string file)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        string? l;
        while ((l = sr.ReadLine()) != null) yield return l;
    }

    static void Exec(SqliteConnection db, string sql)
    {
        using var c = db.CreateCommand();
        c.CommandText = sql;
        c.ExecuteNonQuery();
    }

    static object? Scalar(SqliteConnection db, string sql, params (string, object)[] ps)
    {
        using var c = db.CreateCommand();
        c.CommandText = sql;
        foreach (var (k, v) in ps) c.Parameters.AddWithValue(k, v);
        return c.ExecuteScalar();
    }

    static string? GetMeta(SqliteConnection db, string key) =>
        Scalar(db, "SELECT value FROM meta WHERE key=$k", ("$k", key)) as string;

    static void SetMeta(SqliteConnection db, string key, string value) =>
        Exec(db, $"INSERT OR REPLACE INTO meta(key,value) VALUES('{key}','{value}')");
}
