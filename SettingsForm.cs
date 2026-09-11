namespace TrackerBuddy;

class SettingsForm : Form
{
    static readonly Color Bg = Color.FromArgb(32, 32, 36), Field = Color.FromArgb(45, 45, 52), Fg = Color.FromArgb(240, 240, 245),
        Sub = Color.FromArgb(160, 160, 172), Accent = Color.FromArgb(0, 120, 212);

    Keys hotkey;

    public SettingsForm(Settings s, DateTime monday)
    {
        Text = "Tracker Buddy — Settings";
        Icon = Widget.AppIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        BackColor = Bg;
        ForeColor = Fg;
        Font = new Font("Segoe UI", 10f);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(18);
        hotkey = (Keys)s.Hotkey;

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        void Row(string label, Control c) { grid.Controls.Add(Lbl(label, Fg)); grid.Controls.Add(c); }
        void Span(Control c) { grid.Controls.Add(c); grid.SetColumnSpan(c, 2); }

        Span(Lbl("Hours", Accent, bold: true));
        var week = Pair(s.WeeklyFor(monday), out var weekH, out var weekM);
        var def = Pair(TimeSpan.FromMinutes(s.WeeklyMinutes), out var defH, out var defM);
        var reset = new LinkLabel { Text = "use default", AutoSize = true, LinkColor = Accent, Margin = new Padding(6, 6, 0, 0) };
        reset.Click += (_, _) => { weekH.Value = defH.Value; weekM.Value = defM.Value; };
        week.Controls.Add(reset);
        Row($"This week (from {monday:ddd, MMM d})", week);
        Row("Default weekly hours", def);
        Row("Daily target", Pair(s.Daily(), out var dayH, out var dayM));

        Span(Lbl("Shortcut & alerts", Accent, bold: true, top: 14));
        var box = new TextBox { Text = Settings.Describe(hotkey), ReadOnly = true, Width = 150, BackColor = Field, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, Cursor = Cursors.Hand };
        box.KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;
            if ((e.Modifiers & (Keys.Control | Keys.Alt)) == 0) { box.Text = "use Ctrl or Alt + key"; return; }
            hotkey = e.KeyData;
            box.Text = Settings.Describe(hotkey);
        };
        var hk = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        hk.Controls.AddRange([box, Lbl("click, then press keys")]);
        Row("Show / hide widget", hk);
        var idle = Check("Warn me before the tracker's idle stop", s.IdleWarning);
        var off = Check("Alert me when I'm working but the tracker is off", s.OffAlert);
        Span(idle);
        Span(off);

        var save = Btn("Save", Accent);
        var cancel = Btn("Cancel", Field);
        cancel.DialogResult = DialogResult.Cancel;
        save.Click += (_, _) =>
        {
            s.WeeklyMinutes = Minutes(defH, defM);
            s.DailyMinutes = Minutes(dayH, dayM);
            var thisWeek = Minutes(weekH, weekM);
            if (thisWeek == s.WeeklyMinutes) s.WeekOverrides.Remove(Settings.Key(monday));
            else s.WeekOverrides[Settings.Key(monday)] = thisWeek;
            s.Hotkey = (int)hotkey;
            s.IdleWarning = idle.Checked;
            s.OffAlert = off.Checked;
            DialogResult = DialogResult.OK;
        };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 16, 0, 0) };
        buttons.Controls.AddRange([cancel, save]);
        Span(buttons);

        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(grid);
    }

    static int Minutes(NumericUpDown h, NumericUpDown m) => (int)h.Value * 60 + (int)m.Value;

    static FlowLayoutPanel Pair(TimeSpan t, out NumericUpDown h, out NumericUpDown m)
    {
        h = Num((int)t.TotalHours, 99, 1);
        m = Num(t.Minutes, 59, 5);
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        p.Controls.AddRange([h, Lbl("h"), m, Lbl("m")]);
        return p;
    }

    static NumericUpDown Num(int value, int max, int step) => new()
    {
        Maximum = max, Value = Math.Clamp(value, 0, max), Increment = step, Width = 58,
        BackColor = Field, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Right,
    };

    static Label Lbl(string text, Color? color = null, bool bold = false, int top = 6) => new()
    {
        Text = text, AutoSize = true, UseMnemonic = false, ForeColor = color ?? Sub, Margin = new Padding(3, top, 10, 0),
        Font = new Font("Segoe UI", 10f, bold ? FontStyle.Bold : FontStyle.Regular),
    };

    static CheckBox Check(string text, bool on) => new() { Text = text, Checked = on, AutoSize = true, ForeColor = Fg, Margin = new Padding(3, 6, 3, 0) };

    static Button Btn(string text, Color back)
    {
        var b = new Button { Text = text, AutoSize = true, BackColor = back, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Padding = new Padding(10, 2, 10, 2), Margin = new Padding(8, 0, 0, 0) };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }
}
