using Velopack;
using Velopack.Sources;

namespace TrackerBuddy;

// Copies installed with Setup.exe update themselves from GitHub Releases; the dev build (bin\) skips this.
static class Updater
{
    const string RepoUrl = "https://github.com/meet09-satva/tracker-buddy";
    static readonly UpdateManager Mgr = new(new GithubSource(RepoUrl, null, false));

    public static string Version => Mgr.IsInstalled ? $"v{Mgr.CurrentVersion}" : "dev build";

    public static async Task CheckAsync(NotifyIcon tray, bool manual)
    {
        if (!Mgr.IsInstalled)
        {
            if (manual) tray.ShowBalloonTip(5_000, "Updates", "This is a dev build. Install with Setup.exe to get updates.", ToolTipIcon.Info);
            return;
        }
        try
        {
            var update = await Mgr.CheckForUpdatesAsync();
            if (update == null)
            {
                if (manual) tray.ShowBalloonTip(5_000, "Tracker Buddy is up to date", Version, ToolTipIcon.Info);
                return;
            }
            tray.ShowBalloonTip(5_000, "Updating Tracker Buddy", $"Installing v{update.TargetFullRelease.Version}…", ToolTipIcon.Info);
            await Mgr.DownloadUpdatesAsync(update);
            tray.Visible = false;
            Mgr.ApplyUpdatesAndRestart(update.TargetFullRelease);
        }
        catch (Exception ex)
        {
            // Offline or GitHub unreachable: the next check (6 h or next start) retries.
            if (manual) tray.ShowBalloonTip(5_000, "Update check failed", ex.Message, ToolTipIcon.Warning);
        }
    }
}
