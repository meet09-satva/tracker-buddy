using System.Globalization;
using System.Text.Json;

namespace TrackerBuddy;

class Settings
{
    public int DailyMinutes { get; set; } = 8 * 60 + 30;
    public int WeeklyMinutes { get; set; } = 42 * 60 + 30;
    public Dictionary<string, int> WeekOverrides { get; set; } = new(); // Monday "yyyy-MM-dd" → required minutes
    public int HalfDayMinutes { get; set; } = 4 * 60 + 30;
    public Dictionary<string, int> DayOverrides { get; set; } = new(); // "yyyy-MM-dd" → target minutes (half day)
    public int Hotkey { get; set; } = (int)(Keys.Control | Keys.Alt | Keys.B);
    public bool IdleWarning { get; set; } = true;
    public bool OffAlert { get; set; } = true;
    public bool MergeManual { get; set; } = false; // pull portal manual logs via the tracker's token
    public bool ShowWeeklyBar { get; set; } = true; // card row; week view (calendar icon) always available regardless

    static readonly string JsonPath = Path.Combine(History.Dir, "settings.json");

    public TimeSpan Daily() => TimeSpan.FromMinutes(DailyMinutes);

    public TimeSpan Daily(DateTime day) => TimeSpan.FromMinutes(DayOverrides.TryGetValue(Key(day), out var m) ? m : DailyMinutes);

    public bool HalfDay(DateTime day) => DayOverrides.ContainsKey(Key(day));

    public void SetHalfDay(DateTime day, bool on)
    {
        if (on) DayOverrides[Key(day)] = HalfDayMinutes;
        else DayOverrides.Remove(Key(day));
    }

    // An explicit week override wins; otherwise the default week shrinks by each half day's cut.
    public TimeSpan WeeklyFor(DateTime monday)
    {
        if (WeekOverrides.TryGetValue(Key(monday), out var m)) return TimeSpan.FromMinutes(m);
        var cut = Enumerable.Range(0, 7).Sum(i => DailyMinutes - (int)Daily(monday.AddDays(i)).TotalMinutes);
        return TimeSpan.FromMinutes(WeeklyMinutes - cut);
    }

    public static string Key(DateTime monday) => monday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Describe(Keys k) => new KeysConverter().ConvertToString(k) ?? k.ToString();

    public static Settings Load()
    {
        try { return File.Exists(JsonPath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(JsonPath)) ?? new() : new(); }
        catch (JsonException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(History.Dir);
        File.WriteAllText(JsonPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
