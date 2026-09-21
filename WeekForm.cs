namespace TrackerBuddy;

class WeekForm : Form
{
    static readonly Color Bg = Color.FromArgb(32, 32, 36), Header = Color.FromArgb(45, 45, 52), Fg = Color.FromArgb(240, 240, 245),
        Sub = Color.FromArgb(160, 160, 172), Good = Color.FromArgb(108, 203, 95), Bad = Color.FromArgb(255, 120, 120);

    // ponytail: current week only, add prev/next buttons when older weeks matter
    // manual = portal-entered minutes per day (not in history); added to worked, fills gaps like the card.
    public WeekForm(Dictionary<DateTime, Day> days, Settings settings, Dictionary<DateTime, TimeSpan>? manual = null)
    {
        var today = DateTime.Today;
        var monday = Calc.Monday(today);
        var daily = settings.Daily();
        var required = settings.WeeklyFor(monday);
        Text = $"Tracker Buddy — week of {monday:MMM d}";
        Icon = Widget.AppIcon;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(590, 262);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg;

        var lv = new ListView
        {
            View = View.Details, Dock = DockStyle.Fill, FullRowSelect = true, BorderStyle = BorderStyle.None,
            BackColor = Bg, ForeColor = Fg, OwnerDraw = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
        };
        lv.DrawColumnHeader += (_, e) =>
        {
            using var b = new SolidBrush(Header);
            e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header!.Text, Font, Rectangle.Inflate(e.Bounds, -6, 0), Sub, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };
        lv.DrawItem += (_, e) => e.DrawDefault = true;
        lv.DrawSubItem += (_, e) => e.DrawDefault = true;
        foreach (var (name, width) in new[] { ("Day", 160), ("First start", 95), ("Last stop", 95), ("Worked", 120), ($"vs {Calc.Hm(daily)}", 110) })
            lv.Columns.Add(name, width);

        TimeSpan worked = default;
        for (var i = 0; i < 7; i++)
        {
            var d = monday.AddDays(i);
            var label = d.ToString("ddd, MMM d") + (d == today ? " (today)" : "");
            var man = manual != null && manual.TryGetValue(d, out var mm) ? mm : TimeSpan.Zero;
            var has = days.TryGetValue(d, out var day);
            if (!has && man == TimeSpan.Zero)
            {
                lv.Items.Add(new ListViewItem(new[] { label, "—", "—", "—", "" }) { ForeColor = Sub });
                continue;
            }
            var dayWorked = (has ? day.Worked : TimeSpan.Zero) + man;
            var diff = dayWorked - daily;
            worked += dayWorked;
            var workedText = Calc.Hm(dayWorked) + (man > TimeSpan.Zero ? $" (+{(int)man.TotalMinutes}m)" : "");
            var item = new ListViewItem(new[] { label, has ? day.First.ToString("h:mm tt") : "—", has ? day.Last.ToString("h:mm tt") : "—", workedText, Calc.Signed(diff) })
            { UseItemStyleForSubItems = false };
            item.SubItems[4].ForeColor = diff < TimeSpan.Zero ? Bad : Good;
            lv.Items.Add(item);
        }

        var left = required - worked;
        var total = new ListViewItem(new[] { "Week", "", "", $"{Calc.Hm(worked)} / {Calc.Hm(required)}", left > TimeSpan.Zero ? $"{Calc.Hm(left)} left" : $"+{Calc.Hm(-left)} over" })
        { Font = new Font(Font, FontStyle.Bold), UseItemStyleForSubItems = false };
        foreach (ListViewItem.ListViewSubItem sub in total.SubItems) sub.Font = total.Font;
        total.SubItems[4].ForeColor = left > TimeSpan.Zero ? Bad : Good;
        lv.Items.Add(total);
        Controls.Add(lv);
    }
}
