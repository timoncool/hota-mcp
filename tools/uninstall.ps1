# Removes the HotA MCP add-on and restores the stock launcher behaviour.
# Only files this add-on installed and the autostart entry it created are touched.
[CmdletBinding()]
param(
    [string]$InstallPath = (Join-Path $env:LOCALAPPDATA 'Programs\HotaMcp'),
    # Keep the service state folder (journal, plan, captures).
    [switch]$KeepState
)

$ErrorActionPreference = 'Stop'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runName = 'HotaMcp'

if (Get-ItemProperty -Path $runKey -Name $runName -ErrorAction SilentlyContinue) {
    Remove-ItemProperty -Path $runKey -Name $runName
    Write-Host 'Autostart entry removed.'
}

# Take the tab out of a running launcher first, so the launcher is left with its own pages only.
$dll = Join-Path $InstallPath 'app\hota_launcher_tab.dll'
$attach = Join-Path $InstallPath 'app\launcher-attach.exe'
$launcher = Get-Process HD_Launcher -ErrorAction SilentlyContinue | Select-Object -First 1
if ($launcher -and (Test-Path $attach)) {
    & $attach $launcher.Id --detach | Write-Host
}

Get-Process launcher-attach -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process HotaMcp -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

if (Test-Path $InstallPath) {
    Remove-Item $InstallPath -Recurse -Force
    Write-Host "Removed $InstallPath"
}

$state = Join-Path $env:LOCALAPPDATA 'HotaMcp'
if (-not $KeepState -and (Test-Path $state)) {
    Remove-Item $state -Recurse -Force
    Write-Host "Removed service state $state"
} elseif (Test-Path $state) {
    Write-Host "Service state kept at $state"
}

Write-Host 'The HD Launcher is back to its stock pages. The game was not modified.'
