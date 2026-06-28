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
    const int ParserVersion = 1;   // erhöhen, wenn der Parser neue Felder/Events liefert

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

    public static int SessionCount()
    {
        using var db = new SqliteConnection(Conn);
        db.Open();
        return Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM sessions") ?? 0);
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
