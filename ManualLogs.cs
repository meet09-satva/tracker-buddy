using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TrackerBuddy;

// Manual timesheet entries (added on the OM web portal) never reach the local tracker DB —
// the tracker exe only uploads logs, never downloads them. But the tracker's saved bearer token
// authenticates the portal's own grid endpoint, read-only against your own user id. We fetch that
// grid and keep only the rows flagged "M" (Manual); tracked rows are already in the local DB.
// ponytail: HTML-scrape the grid (no public API); if the portal markup changes, Parse yields nothing
//           and Buddy silently falls back to tracked-only — no crash, no wrong numbers.
static partial class ManualLogs
{
    const string BaseUrl = "https://om.satvasolutions.com";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    static string? Token()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var k = hklm.OpenSubKey(@"Software\TimeTracker\UserKeys");
                if (k?.GetValue("AcccessToken") is string t && t.Length > 0) return t; // tracker's own typo
            }
            catch { /* try the other view */ }
        return null;
    }

    // Manual minutes per day over [from, to]. Empty on any failure: no token, offline, or auth expired
    // (the portal 302s to a login page that has no grid rows) — the caller then shows tracked-only.
    public static async Task<Dictionary<DateTime, TimeSpan>> ByDayAsync(int userId, DateTime from, DateTime to)
    {
        var token = Token();
        if (string.IsNullOrEmpty(token)) return new();
        try
        {
            static string D(DateTime d) => d.ToString("dd MMM ,yyyy", CultureInfo.InvariantCulture); // "15 Sep ,2026"
            var range = Uri.EscapeDataString($"{D(from)} - {D(to)}");
            var url = $"{BaseUrl}/TimeSheetManagement/ManageTimeLogs?btnSearch=Search&UserId={userId}&UserIdList={userId}&FillterDate={range}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Authorization", "bearer " + token);
            req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return new();
            return Parse(await resp.Content.ReadAsStringAsync());
        }
        catch { return new(); } // offline / DNS / TLS: skip the overlay this cycle
    }

    // Grid rows look like: date | project | name | note | dur | dur | flag(U/I) | status [| "M"].
    // A standalone "M" cell means the row was entered manually.
    public static Dictionary<DateTime, TimeSpan> Parse(string html)
    {
        var byDay = new Dictionary<DateTime, TimeSpan>();
        foreach (Match row in RowRx().Matches(html))
        {
            var cells = CellRx().Matches(row.Groups[1].Value)
                .Select(m => WebUtility.HtmlDecode(TagRx().Replace(m.Groups[1].Value, " ")).Replace(" ", " "))
                .Select(s => Regex.Replace(s, @"\s+", " ").Trim())
                .ToList();
            if (!cells.Any(c => c == "M")) continue; // manual flag; multi-day grid also prepends a checkbox cell
            var dateCell = cells.FirstOrDefault(c => DateRx().IsMatch(c));
            if (dateCell == null || !DateTime.TryParseExact(dateCell, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            var dur = cells.FirstOrDefault(c => DurRx().IsMatch(c));
            if (dur == null || !TimeSpan.TryParse(dur, CultureInfo.InvariantCulture, out var t)) continue;
            byDay[date.Date] = byDay.TryGetValue(date.Date, out var sum) ? sum + t : t;
        }
        return byDay;
    }

    [GeneratedRegex("<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline)] private static partial Regex RowRx();
    [GeneratedRegex("<td[^>]*>(.*?)</td>", RegexOptions.Singleline)] private static partial Regex CellRx();
    [GeneratedRegex("<[^>]+>")] private static partial Regex TagRx();
    [GeneratedRegex(@"^\d{1,2}:\d{2}$")] private static partial Regex DurRx();
    [GeneratedRegex(@"^\d{2}/\d{2}/\d{4}$")] private static partial Regex DateRx();

    public static void SelfTest()
    {
        // One manual row (M flag, 00:40) + one tracked row (no M) → only the manual one counts.
        // Row 2 is single-day shape (date first); row 3 is multi-day shape (leading checkbox cell).
        var html = """
        <table><tbody>
        <tr><td>15/09/2026</td><td>STV: Internal</td><td>Meet P.</td><td>Meeting</td><td>01:45</td><td>01:45</td><td>U</td><td>Working</td></tr>
        <tr><td>15/09/2026</td><td>STV: Internal</td><td>Meet P.</td><td>KT with Harsh</td><td>00:40</td><td>00:40</td><td>U</td><td>Completed</td><td>M</td></tr>
        <tr><td><input type="checkbox" value="123"></td><td data-title="Date">14/09/2026</td><td>STV: Internal</td><td>Meet P.</td><td>Call</td><td>00:20</td><td>00:20</td><td>U</td><td>Done</td><td>M</td></tr>
        </tbody></table>
        """;
        var byDay = Parse(html);
        if (byDay.Count != 2) throw new Exception("FAIL: manual row count");
        if (byDay[new DateTime(2026, 9, 15)] != TimeSpan.FromMinutes(40)) throw new Exception("FAIL: manual 15th");
        if (byDay[new DateTime(2026, 9, 14)] != TimeSpan.FromMinutes(20)) throw new Exception("FAIL: manual 14th");
        if (Parse("<tr><td>nope</td></tr>").Count != 0) throw new Exception("FAIL: junk rows ignored");
    }
}
