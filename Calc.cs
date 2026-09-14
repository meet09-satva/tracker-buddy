namespace TrackerBuddy;

record Log(int Id, DateTime Start, DateTime End);

record Status(TimeSpan Worked, TimeSpan Left, DateTime FinishAt, TimeSpan Idle, bool Running);

record Day(DateTime Date, DateTime First, DateTime Last, TimeSpan Worked, TimeSpan Idle);

static class Calc
{
    public static readonly TimeSpan DefaultDaily = TimeSpan.FromMinutes(8 * 60 + 30);

    // The tracker rewrites its persist file about every 63 s while running; older than this = stopped.
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    // DB EndDateTime of the running log lags until stop; the persist file has its live minutes.
    static TimeSpan Dur(Log l, int? persistLogId, int persistMinutes)
    {
        var db = l.End - l.Start;
        var live = l.Id == persistLogId ? TimeSpan.FromMinutes(persistMinutes) : TimeSpan.Zero;
        return db > live ? db : live;
    }

    public static Status Compute(IReadOnlyList<Log> logs, int? persistLogId, int persistMinutes, bool running, DateTime now, TimeSpan target)
    {
        var worked = TimeSpan.Zero;
        foreach (var l in logs) worked += Dur(l, persistLogId, persistMinutes);
        var left = worked >= target ? TimeSpan.Zero : target - worked;
        var idle = logs.Count == 0 ? TimeSpan.Zero : now - logs.Min(l => l.Start) - worked;
        if (idle < TimeSpan.Zero) idle = TimeSpan.Zero;
        return new Status(worked, left, now + left, idle, running);
    }

    // One day's history row; idle = gaps between first start and last stop.
    public static Day Summarize(IReadOnlyList<Log> logs, int? persistLogId, int persistMinutes)
    {
        var worked = TimeSpan.Zero;
        var last = DateTime.MinValue;
        foreach (var l in logs)
        {
            var d = Dur(l, persistLogId, persistMinutes);
            worked += d;
            if (l.Start + d > last) last = l.Start + d;
        }
        var first = logs.Min(l => l.Start);
        var idle = last - first - worked;
        return new Day(first.Date, first, last, worked, idle < TimeSpan.Zero ? TimeSpan.Zero : idle);
    }

    public static DateTime Monday(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    public static TimeSpan WeekWorked(IEnumerable<Day> days, DateTime monday) =>
        days.Where(d => d.Date >= monday && d.Date < monday.AddDays(7)).Aggregate(TimeSpan.Zero, (sum, d) => sum + d.Worked);

    // Tracker 2.7 hard-codes this (DashBoard.Idle_Timer_Tick: GetIdleTime() > 600000).
    public static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan WarnAt = IdleLimit - TimeSpan.FromMinutes(2);

    // On idle stop the tracker ends the log where idle began, so the persist minutes counted during idle are dropped.
    // Leave them out while running; they come back if input returns before the limit.
    public static int LiveMinutes(int persistMinutes, bool running, TimeSpan idle) =>
        running ? Math.Max(0, persistMinutes - (int)idle.TotalMinutes) : persistMinutes;

    public static string Hm(TimeSpan t) => $"{(int)t.TotalHours}:{t.Minutes:00}";

    public static string Signed(TimeSpan t) => (t < TimeSpan.Zero ? "−" : "+") + Hm(t.Duration());

    public static void SelfTest()
    {
        DateTime T(int h, int m) => new(2026, 9, 11, h, m, 0);
        TimeSpan Min(int m) => TimeSpan.FromMinutes(m);
        var today = new List<Log> { new(1, T(10, 3), T(10, 3)), new(2, T(10, 7), T(10, 38)), new(3, T(11, 4), T(11, 4)) };

        // Running: 0 + 31 + 39 live minutes = 70
        var s = Compute(today, 3, 39, true, T(11, 45), DefaultDaily);
        Check(s.Worked == Min(70), "worked");
        Check(s.Left == Min(440), "left");
        Check(s.FinishAt == T(19, 5), "finish");
        Check(s.Idle == Min(32), "idle"); // 102 min span - 70 worked
        Check(Compute(today, 3, 39, true, T(11, 45), Min(480)).FinishAt == T(18, 35), "custom daily target");

        // History row: last activity 11:43 (11:04 + 39 live), 100 min span - 70 worked
        var day = Summarize(today, 3, 39);
        Check(day.First == T(10, 3) && day.Last == T(11, 43) && day.Worked == Min(70) && day.Idle == Min(30), "summary");

        // DB rows win while the day's first log is still there; a wipe (first log gone) never shrinks history
        var h = new Dictionary<DateTime, Day>();
        Check(History.Merge(h, day), "merge new");
        Check(History.Merge(h, day with { Worked = Min(65) }) && h[day.Date].Worked == Min(65), "merge takes db correction");
        Check(!History.Merge(h, day with { First = T(11, 4), Worked = Min(39) }) && h[day.Date].Worked == Min(65), "merge survives wipe");

        // Week: Mon Sep 7 .. Sun Sep 13; the Sunday before must not count
        var mon = new DateTime(2026, 9, 7);
        Check(Monday(T(10, 0)) == mon && Monday(mon) == mon && Monday(new DateTime(2026, 9, 13, 23, 0, 0)) == mon, "monday");
        var week = new[] { day, day with { Date = mon, Worked = Min(480) }, day with { Date = mon.AddDays(-1), Worked = Min(300) } };
        Check(WeekWorked(week, mon) == Min(550), "week worked");

        // Per-week override of required hours
        var st = new Settings();
        st.WeekOverrides[Settings.Key(mon)] = 40 * 60;
        Check(st.WeeklyFor(mon) == Min(2400) && st.WeeklyFor(mon.AddDays(7)) == Min(2550), "week override");

        // After stop the DB end is updated; must not double count with the persist minutes
        today[2] = new(3, T(11, 4), T(11, 43));
        Check(Compute(today, 3, 39, false, T(12, 0), DefaultDaily).Worked == Min(70), "no double count");

        // Target reached
        var done = Compute([new(9, T(9, 0), T(18, 0))], null, 0, false, T(18, 5), DefaultDaily);
        Check(done.Left == TimeSpan.Zero && done.FinishAt == T(18, 5), "done");

        // Nothing yet
        var none = Compute([], null, 0, false, T(9, 0), DefaultDaily);
        Check(none.Worked == TimeSpan.Zero && none.Idle == TimeSpan.Zero && none.Left == DefaultDaily, "empty");

        // Idle minutes while running are not counted (tracker removes them on idle stop)
        Check(LiveMinutes(49, true, TimeSpan.FromSeconds(9 * 60 + 40)) == 40, "live minus idle");
        Check(LiveMinutes(49, true, TimeSpan.FromSeconds(50)) == 49, "short idle kept");
        Check(LiveMinutes(3, true, Min(9)) == 0 && LiveMinutes(49, false, Min(9)) == 49, "live clamp / stopped");
        Check(WarnAt == Min(8), "warn at");
        Check(Signed(Min(-65)) == "−1:05" && Signed(Min(12)) == "+0:12" && Hm(Min(2550)) == "42:30", "format");
    }

    static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception("FAIL: " + name);
    }
}
