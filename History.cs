using System.Globalization;

namespace TrackerBuddy;

// Own copy of daily totals: the tracker's local DB can be wiped (DeleteAllRecord / ReSeedAllTables).
static class History
{
    public static readonly string Dir =Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrackerBuddy");
    static readonly string CsvPath = Path.Combine(Dir, "history.csv");
    static readonly string LimitPath = Path.Combine(Dir, "idle-limit.txt");
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

    // Keeps the bigger day so a DB wipe never shrinks history. Returns true if something changed.
    public static bool Merge(Dictionary<DateTime, Day> days, Day d)
    {
        if (days.TryGetValue(d.Date, out var old) && (old == d || old.Worked > d.Worked)) return false;
        days[d.Date] = d;
        return true;
    }

    public static int? LoadLimit() =>
        File.Exists(LimitPath) && int.TryParse(File.ReadAllText(LimitPath).Trim(), out var m) ? m : null;

    public static void SaveLimit(int minutes)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(LimitPath, minutes.ToString(Inv));
    }
}
