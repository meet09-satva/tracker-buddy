using System.Globalization;

namespace TrackerBuddy;

// Own copy of daily totals: the tracker's local DB can be wiped (DeleteAllRecord / ReSeedAllTables).
static class History
{
    public static readonly string Dir =Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrackerBuddy");
    static readonly string CsvPath = Path.Combine(Dir, "history.csv");
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static Dictionary<DateTime, Day> Load()
    {
        var days = new Dictionary<DateTime, Day>();
        if (!File.Exists(CsvPath)) return days;
        foreach (var line in File.ReadLines(CsvPath).Skip(1))
        {
            var p = line.Split(',');
            if (p.Length < 5) continue;
            var date = DateTime.ParseExact(p[0], "yyyy-MM-dd", Inv);
            days[date] = new Day(date, date + TimeSpan.Parse(p[1], Inv), date + TimeSpan.Parse(p[2], Inv),
                TimeSpan.FromMinutes(int.Parse(p[3], Inv)), TimeSpan.FromMinutes(int.Parse(p[4], Inv)));
        }
        return days;
    }

    public static void Save(Dictionary<DateTime, Day> days)
    {
        Directory.CreateDirectory(Dir);
        var lines = days.Values.OrderBy(d => d.Date).Select(d => FormattableString.Invariant(
            $"{d.Date:yyyy-MM-dd},{d.First:HH:mm},{d.Last:HH:mm},{(int)d.Worked.TotalMinutes},{(int)d.Idle.TotalMinutes}"));
        File.WriteAllLines(CsvPath, lines.Prepend("date,first_start,last_stop,worked_min,idle_min"));
    }

    // The DB is the truth while it still has the day's first log (it may correct minutes down, e.g. idle removed).
    // Once that log is gone the DB was wiped, so only a bigger day replaces ours. Returns true if something changed.
    public static bool Merge(Dictionary<DateTime, Day> days, Day d)
    {
        if (days.TryGetValue(d.Date, out var old) && (old == d || (d.First > old.First && old.Worked > d.Worked))) return false;
        days[d.Date] = d;
        return true;
    }

    // Raw log cache: logout runs DeleteAllRecord on the tracker DB, so the day's earlier logs vanish from it.
    // We keep every log we've seen (start → live end) and union it back in. Keyed by start (ids get reseeded).
    static readonly string LogsPath = Path.Combine(Dir, "logs.csv");

    public static Dictionary<DateTime, DateTime> LoadLogs()
    {
        var logs = new Dictionary<DateTime, DateTime>();
        if (!File.Exists(LogsPath)) return logs;
        foreach (var line in File.ReadLines(LogsPath))
        {
            var p = line.Split(',');
            if (p.Length == 2 && DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm", Inv, DateTimeStyles.None, out var s)
                && DateTime.TryParseExact(p[1], "yyyy-MM-dd HH:mm", Inv, DateTimeStyles.None, out var e)) logs[s] = e;
        }
        return logs;
    }

    public static void SaveLogs(Dictionary<DateTime, DateTime> logs, DateTime keepFrom)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllLines(LogsPath, logs.Where(kv => kv.Key >= keepFrom).OrderBy(kv => kv.Key)
            .Select(kv => FormattableString.Invariant($"{kv.Key:yyyy-MM-dd HH:mm},{kv.Value:yyyy-MM-dd HH:mm}")));
    }

    // DB rows are the truth while present (they may shrink when idle is removed). Returns true if the cache changed.
    public static bool Remember(Dictionary<DateTime, DateTime> cache, IEnumerable<Log> db, int? persistLogId, int persistMinutes)
    {
        var changed = false;
        foreach (var l in db)
        {
            var end = l.Start + Calc.Dur(l, persistLogId, persistMinutes);
            if (cache.TryGetValue(l.Start, out var old) && old == end) continue;
            cache[l.Start] = end;
            changed = true;
        }
        return changed;
    }

    public static List<Log> Union(Dictionary<DateTime, DateTime> cache, List<Log> db)
    {
        var starts = db.Select(l => l.Start).ToHashSet();
        return db.Concat(cache.Where(kv => !starts.Contains(kv.Key)).Select(kv => new Log(0, kv.Key, kv.Value))).ToList();
    }
}
