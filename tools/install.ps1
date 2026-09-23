# Installs the HotA MCP add-on over an existing GOG Complete + HotA + HD Mod installation.
#
# Nothing inside the game folder is written. The add-on lives in the user's own program folder and
# starts from its «HotA MCP» shortcut: the service comes up, opens the player's own HD Launcher, puts
# the MCP tab into it and starts the game through the launcher's Play button. Nothing is left in the
# Windows autostart. Uninstall removes exactly these files and shortcuts.
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

$tab = Join-Path $repo 'native\launcher\build\v7'
if (-not (Test-Path (Join-Path $tab 'hota_launcher_tab.dll'))) {
    throw "Launcher tab binaries missing in $tab. Run native\launcher\build.cmd first."
}

$app = Join-Path $InstallPath 'app'

# A running service holds its own binaries open, so an upgrade has to ask it to stop first. The
# service exposes a control pipe per launcher; when it does not answer, the process is ended
# directly, because leaving a half-published folder behind is worse than a restart.
$running = Get-Process HotaMcp -ErrorAction SilentlyContinue
if ($running) {
    Write-Host 'Stopping the running MCP service...'
    foreach ($process in $running) {
        $stopped = $false
        foreach ($launcher in (Get-Process HD_Launcher -ErrorAction SilentlyContinue)) {
            try {
                $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', ("hota-mcp-control-{0}" -f $launcher.Id), [IO.Pipes.PipeDirection]::InOut)
                $pipe.Connect(1000)
                $bytes = [Text.Encoding]::UTF8.GetBytes('stop')
                $pipe.Write($bytes, 0, $bytes.Length)
                $null = $pipe.ReadByte()
                $pipe.Dispose()
                $stopped = $true
            } catch { }
        }
        if (-not $process.WaitForExit(4000)) {
            if (-not $stopped) { Write-Host '  control pipe did not answer; ending the process' }
            try { $process.Kill() } catch { }
            $null = $process.WaitForExit(4000)
        }
    }
}

if (-not $NoBuild) {
    Write-Host 'Publishing the MCP service...'
    & dotnet publish (Join-Path $repo 'src\HotaMcp\HotaMcp.csproj') -c Release -o $app -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
} elseif (-not (Test-Path (Join-Path $app 'HotaMcp.exe'))) {
    throw "No published service in $app and -NoBuild was given."
}

# The tab starts the service from its own directory, so both live side by side. The tab DLL is
# injected into a running launcher and is therefore locked while that launcher lives; replacing an
# identical file would gain nothing, so it is copied only when it actually differs, and a locked
# newer version is reported instead of failing the whole install.
foreach ($binary in @('hota_launcher_tab.dll', 'launcher-attach.exe')) {
    $from = Join-Path $tab $binary
    $to = Join-Path $app $binary
    $same = (Test-Path $to) -and ((Get-FileHash $from).Hash -eq (Get-FileHash $to).Hash)
    if ($same) { continue }
    try {
        Copy-Item $from $to -Force
    } catch [System.IO.IOException] {
        Write-Warning ("{0} is in use by the running launcher and was not replaced. Close the HD Launcher and run this installer again to pick up the new tab." -f $binary)
    }
}

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

# Earlier versions kept a watcher in the Windows autostart; the start is now the shortcut below.
if (Get-ItemProperty -Path $runKey -Name $runName -ErrorAction SilentlyContinue) {
    Remove-ItemProperty -Path $runKey -Name $runName
    Write-Host 'Old autostart watcher entry removed.'
}
Get-CimInstance Win32_Process -Filter "Name='launcher-attach.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like '*--watch*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force }

# «HotA MCP»: the service starts, opens the player's HD Launcher with the MCP tab, then the game.
$shell = New-Object -ComObject WScript.Shell
$places = @([Environment]::GetFolderPath('Desktop'), (Join-Path ([Environment]::GetFolderPath('Programs')) 'HotA MCP'))
foreach ($place in $places) {
    New-Item -ItemType Directory -Path $place -Force | Out-Null
    $link = $shell.CreateShortcut((Join-Path $place 'HotA MCP.lnk'))
    $link.TargetPath = Join-Path $app 'HotaMcp.exe'
    $link.Arguments = '--launch'
    $link.WorkingDirectory = $app
    $link.WindowStyle = 7
    $link.IconLocation = (Join-Path $game 'HD_Launcher.exe') + ',0'
    $link.Description = 'HotA MCP: service, HD Launcher with the MCP tab, then the game'
    $link.Save()
}

Write-Host ''
Write-Host "Installed to $InstallPath"
Write-Host 'Start everything with the «HotA MCP» shortcut (desktop and Start menu): service, HD Launcher with the MCP tab, game.'
Write-Host 'Nothing in the game folder was modified. Run tools\uninstall.ps1 to remove.'
