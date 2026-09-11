using System.Drawing.Drawing2D;
using System.Media;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;

namespace TrackerBuddy;

class Widget : Form
{
    const string TrackerDir = @"C:\Program Files (x86)\Satva\TimeTracker";
    const string ConnStr = @"Server=(localdb)\MSSQLLocalDB;Database=Time_Tracker;Integrated Security=true;Encrypt=false;Connect Timeout=10;Application Name=TrackerBuddy";
    const int W = 310, PillH = 38, CardH = 172, P = 14;

    public static readonly Icon AppIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;

    static readonly Color Bg = Color.FromArgb(28, 28, 32), Border = Color.FromArgb(64, 64, 72), Fg = Color.FromArgb(242, 242, 246),
        Sub = Color.FromArgb(158, 158, 170), Faint = Color.FromArgb(105, 105, 115), Track = Color.FromArgb(54, 54, 62),
        Green = Color.FromArgb(108, 203, 95), Orange = Color.FromArgb(255, 170, 68), Red = Color.FromArgb(255, 99, 99),
        Blue = Color.FromArgb(96, 205, 255), Accent = Color.FromArgb(0, 183, 195);

    static readonly Font Small = Ui(9f), Body = Ui(10f), Bold = Ui(10f, FontStyle.Bold), Big = Ui(22f, FontStyle.Bold, display: true),
        Icons = new(Has("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets", 12f);

    static Font Ui(float size, FontStyle style = FontStyle.Regular, bool display = false)
    {
        var family = display ? "Segoe UI Variable Display" : "Segoe UI Variable Text";
        return new Font(Has(family) ? family : "Segoe UI", size, style);
    }

    static bool Has(string family) => FontFamily.Families.Any(f => f.Name == family);

    readonly NotifyIcon tray = new() { Visible = true, Text = "Tracker Buddy" };
    readonly ContextMenuStrip menu = new();
    readonly ToolStripMenuItem toggleItem = new("Show / Hide");
    readonly System.Windows.Forms.Timer timer = new() { Interval = 30_000 }, idleTimer = new() { Interval = 5_000 },
        updateTimer = new() { Interval = 6 * 60 * 60 * 1000 };
    readonly Settings settings = Settings.Load();
    Status? last;
    TimeSpan weekWorked;
    string? warn, error;
    int? idleLimit = History.LoadLimit();
    bool idleWarned, wasRunning, warned, doneAlerted, expanded, dragged, settingsOpen;
    DateTime alertDay;
    DateTime? offAlertAt;
    Point dragFrom;
    Rectangle gearRect, weekRect;
    WeekForm? week;

    public Widget()
    {
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        BackColor = Bg;
        Icon = AppIcon;
        StartPosition = FormStartPosition.Manual;
        var wa = Screen.PrimaryScreen!.WorkingArea;
        Bounds = new Rectangle(wa.Right - W - 12, wa.Bottom - PillH - 12, W, PillH);

        tray.Icon = new Icon(AppIcon, SystemInformation.SmallIconSize);
        toggleItem.Click += (_, _) => ToggleVisible();
        menu.Items.Add(new ToolStripMenuItem($"Tracker Buddy {Updater.Version}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Week view", null, (_, _) => OpenWeek());
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add(toggleItem);
        menu.Items.Add("Refresh", null, (_, _) => Refresh_());
        menu.Items.Add("Check for updates", null, async (_, _) => await Updater.CheckAsync(tray, manual: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => { tray.Visible = false; Application.Exit(); });
        ContextMenuStrip = menu;
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ToggleVisible();

        timer.Tick += (_, _) => Refresh_();
        idleTimer.Tick += (_, _) => CheckIdle();
        updateTimer.Tick += async (_, _) => await Updater.CheckAsync(tray, manual: false);
        timer.Start();
        idleTimer.Start();
        updateTimer.Start();
        Refresh_();
        _ = Updater.CheckAsync(tray, manual: false);
    }

    // Don't steal focus, and stay out of Alt+Tab.
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } // WS_EX_TOOLWINDOW
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var round = 2; // DWMWCP_ROUND: Windows 11 rounded corners + shadow; ignored on Windows 10
        DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
        var border = ColorTranslator.ToWin32(Border);
        DwmSetWindowAttribute(Handle, 34, ref border, sizeof(int));
        RegisterHotkey();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312) ToggleVisible(); // WM_HOTKEY
        base.WndProc(ref m);
    }

    void ToggleVisible()
    {
        Visible = !Visible;
        if (Visible) TopMost = true;
    }

    void RegisterHotkey()
    {
        UnregisterHotKey(Handle, 1);
        var k = (Keys)settings.Hotkey;
        var mod = (k.HasFlag(Keys.Alt) ? 1u : 0) | (k.HasFlag(Keys.Control) ? 2u : 0) | (k.HasFlag(Keys.Shift) ? 4u : 0) | 0x4000u; // MOD_NOREPEAT
        var name = Settings.Describe(k);
        toggleItem.ShortcutKeyDisplayString = name;
        if (!RegisterHotKey(Handle, 1, mod, (uint)(k & Keys.KeyCode)))
            tray.ShowBalloonTip(8_000, "Shortcut not available", $"{name} is used by another app. Pick another in Settings.", ToolTipIcon.Warning);
    }

    static FileInfo? Persist() => new DirectoryInfo(TrackerDir).GetFiles("Persistance_*.xml").MaxBy(f => f.LastWriteTime);

    static bool IsRunning(FileInfo? persist, DateTime now) => persist != null && now - persist.LastWriteTime < Calc.StaleAfter;

    void Refresh_()
    {
        try
        {
            var now = DateTime.Now;
            var persist = Persist();
            int? userId = null, logId = null;
            int minutes = 0;
            if (persist != null)
            {
                var x = XDocument.Load(persist.FullName).Root!;
                userId = (int?)x.Element("UserId");
                logId = (int?)x.Element("TimeLogId");
                minutes = (int?)x.Element("Minutes") ?? 0;
            }

            var logs = ReadLogs(userId, now.Date.AddDays(-7));
            var history = History.Load();
            var changed = false;
            foreach (var g in logs.GroupBy(l => l.Start.Date))
                changed |= History.Merge(history, Calc.Summarize(g.ToList(), logId, minutes));
            if (changed) History.Save(history);

            last = Calc.Compute(logs.Where(l => l.Start.Date == now.Date).ToList(), logId, minutes, IsRunning(persist, now), now, settings.Daily());
            weekWorked = Calc.WeekWorked(history.Values, Calc.Monday(now));
            error = null;
            tray.Text = $"Today {Calc.Hm(last.Worked)}/{Calc.Hm(settings.Daily())} · Week {Calc.Hm(weekWorked)}/{Calc.Hm(WeekRequired())} · Idle {Calc.Hm(last.Idle)}";
            Alerts(last, now);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        Invalidate();
    }

    TimeSpan WeekRequired() => settings.WeeklyFor(Calc.Monday(DateTime.Now));

    static List<Log> ReadLogs(int? userId, DateTime from)
    {
        using var c = new SqlConnection(ConnStr);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"select EmployeeTimeTracker_TimeLog_Id, StartDateTime, EndDateTime from EmployeeTimeTracker_TimeLog
                            where StartDateTime >= @from and (@u is null or UserId = @u)";
        cmd.Parameters.AddWithValue("@from", from);
        cmd.Parameters.AddWithValue("@u", (object?)userId ?? DBNull.Value);
        var logs = new List<Log>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var start = r.GetDateTime(1);
            logs.Add(new Log(r.GetInt32(0), start, r.IsDBNull(2) ? start : r.GetDateTime(2)));
        }
        return logs;
    }

    void CheckIdle()
    {
        var now = DateTime.Now;
        var persist = Persist();
        var running = IsRunning(persist, now);
        var idle = IdleNow();

        if (wasRunning && !running && Calc.LearnLimit(persist!.LastWriteTime, now - idle) is int lim)
        {
            idleLimit = lim;
            History.SaveLimit(lim);
            tray.ShowBalloonTip(10_000, $"Tracker stopped after ~{lim} min idle",
                $"From now on I'll warn you at {(int)Calc.WarnAt(lim).TotalMinutes} min of no activity. Restart the tracker when you're back.", ToolTipIcon.Info);
        }
        if (running != wasRunning) { wasRunning = running; Refresh_(); }

        // Working but the tracker is off: you're at the keyboard, today's target isn't met, the tracker isn't running.
        if (running) offAlertAt = null;
        else if (settings.OffAlert && idle < TimeSpan.FromSeconds(30) && last != null && last.Left > TimeSpan.Zero
                 && (offAlertAt is null || now - offAlertAt > TimeSpan.FromMinutes(10)))
        {
            offAlertAt = now;
            Visible = true;
            SystemSounds.Asterisk.Play();
            tray.ShowBalloonTip(8_000, "Time Tracker is off", "You're working but the tracker isn't running. Start it so this time counts.", ToolTipIcon.Warning);
        }

        var warnAt = Calc.WarnAt(idleLimit);
        string? w = null;
        if (settings.IdleWarning && running && idle >= warnAt)
        {
            var left = idleLimit is int m ? TimeSpan.FromMinutes(m) - idle : (TimeSpan?)null;
            w = $"⚠ No input {Ms(idle)}" + (left is null ? "" : left > TimeSpan.Zero ? $" · stops in ~{Ms(left.Value)}" : " · stopping…");
            if (!idleWarned)
            {
                idleWarned = true;
                Visible = true;
                SystemSounds.Exclamation.Play();
                tray.ShowBalloonTip(8_000, $"No activity for {(int)idle.TotalMinutes} min",
                    "The tracker will stop soon. Move the mouse if you're still working.", ToolTipIcon.Warning);
            }
        }
        else if (idle < warnAt) idleWarned = false;
        if (w != warn) { warn = w; Invalidate(); }

        // Safety net: collapse if the mouse left while a menu was open.
        if (expanded && !menu.Visible && !Bounds.Contains(Cursor.Position)) SetExpanded(false);
    }

    void Alerts(Status s, DateTime now)
    {
        if (alertDay != now.Date) { alertDay = now.Date; warned = doneAlerted = false; }
        if (s.Worked == TimeSpan.Zero) return;
        if (!warned && s.Left > TimeSpan.Zero && s.Left <= TimeSpan.FromMinutes(15))
        {
            warned = true;
            tray.ShowBalloonTip(10_000, "15 minutes left", $"Your {Calc.Hm(settings.Daily())} completes at {s.FinishAt:h:mm tt}", ToolTipIcon.Info);
        }
        if (!doneAlerted && s.Left == TimeSpan.Zero)
        {
            doneAlerted = warned = true;
            tray.ShowBalloonTip(10_000, $"{Calc.Hm(settings.Daily())} hours complete ✔", $"Worked {Calc.Hm(s.Worked)} · Idle {Calc.Hm(s.Idle)}", ToolTipIcon.Info);
        }
    }

    // ---- hover, drag, clicks ----

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); SetExpanded(true); }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!ClientRectangle.Contains(PointToClient(Cursor.Position))) SetExpanded(false);
    }

    void SetExpanded(bool on)
    {
        if (expanded == on) return;
        expanded = on;
        var h = on ? CardH : PillH;
        var wa = Screen.FromControl(this).WorkingArea;
        SetBounds(Left, Math.Max(wa.Top, Bottom - h), W, h); // grow upward, keep the bottom edge
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) { dragFrom = e.Location; dragged = false; }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.Button == MouseButtons.Left)
        {
            var dx = e.X - dragFrom.X;
            var dy = e.Y - dragFrom.Y;
            if (!dragged && Math.Abs(dx) + Math.Abs(dy) < 4) return;
            dragged = true;
            Location += new Size(dx, dy);
        }
        else Cursor = expanded && (gearRect.Contains(e.Location) || weekRect.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || dragged || !expanded) return;
        if (gearRect.Contains(e.Location)) OpenSettings();
        else if (weekRect.Contains(e.Location)) OpenWeek();
    }

    void OpenSettings()
    {
        if (settingsOpen) return;
        settingsOpen = true;
        using var f = new SettingsForm(settings, Calc.Monday(DateTime.Now));
        if (f.ShowDialog(this) == DialogResult.OK)
        {
            settings.Save();
            RegisterHotkey();
            Refresh_();
        }
        settingsOpen = false;
    }

    void OpenWeek()
    {
        if (week is { IsDisposed: false }) { week.Activate(); return; }
        week = new WeekForm(History.Load(), settings);
        week.Show();
    }

    // ---- painting ----

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (error != null || last is null)
        {
            TextRenderer.DrawText(g, error is null ? "Loading…" : "⚠ " + error, Small, new Rectangle(P, 0, W - 2 * P, PillH),
                error is null ? Sub : Red, Bg, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }
        var color = warn != null ? Red : last.Left == TimeSpan.Zero ? Blue : last.Running ? Green : Orange;
        if (expanded) PaintCard(g, last, color);
        else PaintPill(g, last, color);
    }

    void PaintPill(Graphics g, Status s, Color c)
    {
        Dot(g, P + 2, PillH / 2, 5, c);
        var x = P + 14;
        if (warn != null) { Draw(g, warn, Bold, Red, x, 0, PillH); return; }
        x = Draw(g, Calc.Hm(s.Worked), Bold, Fg, x, 0, PillH) + 8;
        Bar(g, new Rectangle(x, PillH / 2 - 3, 64, 6), Ratio(s.Worked, settings.Daily()), c);
        x += 64 + 9;
        var (finish, fc) = s.Left == TimeSpan.Zero ? ("done ✔", Blue) : s.Running ? ($"→ {s.FinishAt:h:mm tt}", Fg) : ($"⏸ {s.FinishAt:h:mm tt}", Orange);
        x = Draw(g, finish, Body, fc, x, 0, PillH) + 6;
        Draw(g, $"idle {Calc.Hm(s.Idle)}", Small, Sub, x, 0, PillH);
    }

    void PaintCard(Graphics g, Status s, Color c)
    {
        var daily = settings.Daily();

        // Status line + icon buttons
        Dot(g, P + 5, 22, 5, c);
        var status = warn ?? (s.Left == TimeSpan.Zero ? "Daily target done" : s.Running ? "Running" : "Stopped — start the tracker");
        Draw(g, status, Bold, warn != null ? Red : s.Running || s.Left == TimeSpan.Zero ? Fg : Orange, P + 16, 10, 24);
        gearRect = new Rectangle(W - P - 24, 10, 24, 24);
        weekRect = new Rectangle(W - P - 54, 10, 24, 24);
        TextRenderer.DrawText(g, "\uE787", Icons, weekRect, Sub, Bg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); // calendar
        TextRenderer.DrawText(g, "\uE713", Icons, gearRect, Sub, Bg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); // settings

        // Worked today + finish time
        var bx = Draw(g, Calc.Hm(s.Worked), Big, Fg, P - 2, 38, 46);
        Draw(g, $"of {Calc.Hm(daily)}", Small, Sub, bx + 4, 52, 28);
        DrawRight(g, s.Left == TimeSpan.Zero ? $"+{Calc.Hm(s.Worked - daily)} extra" : $"{s.FinishAt:h:mm tt}", Bold, s.Left == TimeSpan.Zero ? Blue : Fg, W - P, 42, 20);
        DrawRight(g, s.Left == TimeSpan.Zero ? "today" : $"finish · {Calc.Hm(s.Left)} left", Small, Sub, W - P, 62, 18);
        Bar(g, new Rectangle(P, 88, W - 2 * P, 6), Ratio(s.Worked, daily), c);

        // Week
        var required = WeekRequired();
        var weekLeft = required - weekWorked;
        Draw(g, "This week", Small, Sub, P, 102, 18);
        DrawRight(g, $"{Calc.Hm(weekWorked)} / {Calc.Hm(required)} · " + (weekLeft > TimeSpan.Zero ? $"{Calc.Hm(weekLeft)} left" : "done ✔"), Small, Fg, W - P, 102, 18);
        Bar(g, new Rectangle(P, 124, W - 2 * P, 6), Ratio(weekWorked, required), Accent);

        // Footer
        Draw(g, $"Idle today {Calc.Hm(s.Idle)}", Small, Sub, P, 140, 22);
        DrawRight(g, $"{Settings.Describe((Keys)settings.Hotkey)} to hide", Small, Faint, W - P, 140, 22);
    }

    static int Draw(Graphics g, string text, Font f, Color c, int x, int y, int rowH)
    {
        var size = TextRenderer.MeasureText(g, text, f, Size.Empty, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, text, f, new Point(x, y + (rowH - size.Height) / 2), c, Bg, TextFormatFlags.NoPadding);
        return x + size.Width;
    }

    static void DrawRight(Graphics g, string text, Font f, Color c, int right, int y, int rowH)
    {
        var width = TextRenderer.MeasureText(g, text, f, Size.Empty, TextFormatFlags.NoPadding).Width;
        Draw(g, text, f, c, right - width, y, rowH);
    }

    static double Ratio(TimeSpan part, TimeSpan whole) => whole <= TimeSpan.Zero ? 1 : Math.Clamp(part / whole, 0, 1);

    static void Dot(Graphics g, int cx, int cy, int r, Color c)
    {
        using var b = new SolidBrush(c);
        g.FillEllipse(b, cx - r, cy - r, 2 * r, 2 * r);
    }

    static void Bar(Graphics g, Rectangle r, double ratio, Color c)
    {
        using (var track = new SolidBrush(Track)) using (var path = Pill(r)) g.FillPath(track, path);
        if (ratio <= 0) return;
        var fill = r with { Width = Math.Max(r.Height, (int)(r.Width * ratio)) };
        using var b = new SolidBrush(c);
        using var p = Pill(fill);
        g.FillPath(b, p);
    }

    static GraphicsPath Pill(Rectangle r)
    {
        var p = new GraphicsPath();
        var d = r.Height;
        p.AddArc(r.X, r.Y, d, d, 90, 180);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
        p.CloseFigure();
        return p;
    }

    static string Ms(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        tray.Visible = false;
        UnregisterHotKey(Handle, 1);
        base.OnFormClosed(e);
    }

    // ---- Win32 ----

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    // Same Windows value the tracker uses to decide you're idle.
    static TimeSpan IdleNow()
    {
        var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        GetLastInputInfo(ref li);
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - li.dwTime));
    }
}
