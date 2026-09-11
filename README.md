# Tracker Buddy

A small always-on-top widget for **Satva Time Tracker**: time worked today, when your 8:30 completes,
idle time, weekly hours, and a warning before the tracker's idle auto-stop.

It only **reads** Time Tracker's local data on your PC. It never changes it.

## Install

1. Download **`TrackerBuddyApp-win-Setup.exe`** from the [latest release](https://github.com/meet09-satva/tracker-buddy/releases/latest).
2. Run it. If Windows shows *"Windows protected your PC"*, click **More info → Run anyway** (the app isn't code-signed).
3. The widget opens and a **Tracker Buddy** shortcut is added to your Desktop and Start menu.

Updates install automatically (checked at start and every 6 hours). Right-click the widget → **Check for updates** to check now.

## Use

- **Hover** the pill to see the full card; **drag** it anywhere.
- **Gear** icon → Settings: this week's required hours, default weekly hours, daily target, shortcut, alerts.
- **Calendar** icon → week view.
- **Ctrl+Alt+B** shows / hides the widget (changeable in Settings).
- Uninstall from Windows **Settings → Apps**.

## Release a new version (maintainer)

```powershell
./release.ps1 1.0.1
```

Builds, runs the self-test, packs with [Velopack](https://docs.velopack.io) and publishes a GitHub release.
Needs the .NET 10 SDK, `dotnet tool install -g vpk`, and `gh auth login`.
