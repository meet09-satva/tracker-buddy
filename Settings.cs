using System.Globalization;
using System.Text.Json;

namespace TrackerBuddy;

class Settings
{
    public int DailyMinutes { get; set; } = 8 * 60 + 30;
    public int WeeklyMinutes { get; set; } = 42 * 60 + 30;
    public Dictionary<string, int> WeekOverrides { get; set; } = new(); // Monday "yyyy-MM-dd" → required minutes
    public int Hotkey { get; set; } = (int)(Keys.Control | Keys.Alt | Keys.B);
    public bool IdleWarning { get; set; } = true;
    public bool OffAlert { get; set; } = true;
    public bool MergeManual { get; set; } = false; // pull portal manual logs via the tracker's token
    public bool ShowWeeklyBar { get; set; } = true; // card row; week view (calendar icon) always available regardless

    static readonly string JsonPath = Path.Combine(History.Dir, "settings.json");

    public TimeSpan Daily() => TimeSpan.FromMinutes(DailyMinutes);

    public TimeSpan WeeklyFor(DateTime monday) =>
        TimeSpan.FromMinutes(WeekOverrides.TryGetValue(Key(monday), out var m) ? m : WeeklyMinutes);

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
