# Publishes a new version to GitHub Releases; installed copies pick it up automatically.
# Usage: ./release.ps1 1.0.1
param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$repo = 'https://github.com/meet09-satva/tracker-buddy'
$token = gh auth token
function Check($step) { if ($LASTEXITCODE) { throw "$step failed (exit $LASTEXITCODE)" } }

Remove-Item publish, Releases -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish TrackerBuddy.csproj -c Release --self-contained -r win-x64 -o publish -p:Version=$Version; Check 'publish'
& .\publish\TrackerBuddy.exe --test | Out-Host; Check 'self-test'

# Previous release lets vpk build a small delta update; there is none before the first release.
vpk download github --repoUrl $repo --token $token -o Releases
if ($LASTEXITCODE) { Write-Host 'No previous release found; building full package only.' }

# packId differs from the data folder (%LOCALAPPDATA%\TrackerBuddy) so uninstall never deletes history/settings.
vpk pack -u TrackerBuddyApp -v $Version -p publish -e TrackerBuddy.exe --packTitle 'Tracker Buddy' -i app.ico --shortcuts Desktop,StartMenuRoot -o Releases; Check 'pack'
vpk upload github --repoUrl $repo --token $token --publish --releaseName "Tracker Buddy $Version" --tag "v$Version" -o Releases; Check 'upload'
Write-Host "Released v$Version → $repo/releases/tag/v$Version"
