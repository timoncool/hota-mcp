# Installs the HotA MCP add-on over an existing GOG Complete + HotA + HD Mod installation.
#
# Nothing inside the game folder is written. The add-on lives in the user's own program folder and
# a watcher process attaches the MCP tab to the ordinary HD Launcher every time it opens, including
# after HD Mod updates itself and restarts the launcher. Uninstall removes exactly these files and
# the autostart entry; the launcher returns to its stock behaviour.
[CmdletBinding()]
param(
    # Folder of the HotA installation. Detected from a running launcher when omitted.
    [string]$GamePath,
    # Where the add-on is installed. Per user, no administrator rights needed.
    [string]$InstallPath = (Join-Path $env:LOCALAPPDATA 'Programs\HotaMcp'),
    # Skip building and take the binaries that are already published.
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runName = 'HotaMcp'

function Find-GamePath {
    if ($GamePath) { return $GamePath }
    $launcher = Get-Process HD_Launcher -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($launcher) { return Split-Path -Parent $launcher.Path }
    foreach ($drive in (Get-PSDrive -PSProvider FileSystem).Root) {
        foreach ($candidate in @('HoMM 3 Complete', 'Games\HoMM 3 Complete', 'GOG Games\HoMM 3 Complete')) {
            $path = Join-Path $drive $candidate
            if (Test-Path (Join-Path $path 'h3hota HD.exe')) { return $path }
        }
    }
    throw 'HotA installation not found. Start the HD Launcher or pass -GamePath.'
}

$game = Find-GamePath
foreach ($required in @('h3hota HD.exe', 'HotA.dll', 'HD_Launcher.exe')) {
    if (-not (Test-Path (Join-Path $game $required))) {
        throw "Not a supported installation: $required is missing in $game. Install GOG Heroes III Complete, then HotA, then HD Mod."
    }
}
Write-Host "HotA installation: $game"
foreach ($file in @('h3hota HD.exe', 'HotA.dll', 'HD_Launcher.exe', 'HD_LauncherNative.dll')) {
    $full = Join-Path $game $file
    if (Test-Path $full) { Write-Host ("  {0,-24} {1}" -f $file, (Get-FileHash $full -Algorithm SHA256).Hash) }
}

$tab = Join-Path $repo 'native\launcher\build\v6'
if (-not (Test-Path (Join-Path $tab 'hota_launcher_tab.dll'))) {
    throw "Launcher tab binaries missing in $tab. Run native\launcher\build.cmd first."
}

$app = Join-Path $InstallPath 'app'
if (-not $NoBuild) {
    Write-Host 'Publishing the MCP service...'
    & dotnet publish (Join-Path $repo 'src\HotaMcp\HotaMcp.csproj') -c Release -o $app -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
} elseif (-not (Test-Path (Join-Path $app 'HotaMcp.exe'))) {
    throw "No published service in $app and -NoBuild was given."
}

# The tab starts the service from its own directory, so both live side by side.
Copy-Item (Join-Path $tab 'hota_launcher_tab.dll') $app -Force
Copy-Item (Join-Path $tab 'launcher-attach.exe') $app -Force

# The bridge serves its documentation offline; it is found by walking up from the binaries.
foreach ($tree in @('docs', 'skills')) {
    $destination = Join-Path $InstallPath $tree
    if (Test-Path $destination) { Remove-Item $destination -Recurse -Force }
    Copy-Item (Join-Path $repo $tree) $destination -Recurse -Force
}

# A client connecting to a cold machine needs to know which launcher to raise.
$state = Join-Path $env:LOCALAPPDATA 'HotaMcp'
New-Item -ItemType Directory -Path $state -Force | Out-Null
Set-Content -Path (Join-Path $state 'install.ini') -Encoding utf8 `
    -Value ("Launcher=" + (Join-Path $game 'HD_Launcher.exe'))

$watcher = Join-Path $app 'launcher-attach.exe'
$dll = Join-Path $app 'hota_launcher_tab.dll'
$command = '"{0}" --watch "{1}"' -f $watcher, $dll

Get-Process launcher-attach -ErrorAction SilentlyContinue | Stop-Process -Force
Set-ItemProperty -Path $runKey -Name $runName -Value $command
Start-Process -FilePath $watcher -ArgumentList '--watch', $dll -WindowStyle Hidden

Write-Host ''
Write-Host "Installed to $InstallPath"
Write-Host 'The MCP tab now appears in the HD Launcher every time it is opened.'
Write-Host 'Nothing in the game folder was modified. Run tools\uninstall.ps1 to remove.'
