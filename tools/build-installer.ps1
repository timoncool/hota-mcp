# Builds the Windows installer dist\HotaMcp-Setup-<version>.exe.
#
# The service is published self-contained for win-x64, so the player needs no .NET runtime. The
# stage holds exactly what the installer copies: app (service, native game adapter, launcher tab),
# docs and skills. NSIS 3 (makensis) turns it into a per-user installer.
[CmdletBinding()]
param(
    [string]$Makensis = 'C:\Program Files (x86)\NSIS\makensis.exe'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\HotaMcp\HotaMcp.csproj'
$version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> in $project" }
if (-not (Test-Path $Makensis)) { throw "makensis not found at $Makensis. Install NSIS 3 or pass -Makensis." }

$tab = Join-Path $repo 'native\launcher\build\v7'
foreach ($binary in @('hota_launcher_tab.dll', 'launcher-attach.exe')) {
    if (-not (Test-Path (Join-Path $tab $binary))) { throw "$binary missing in $tab. Run native\launcher\build.cmd first." }
}

$dist = Join-Path $repo 'dist'
$stage = Join-Path $dist 'stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
$app = Join-Path $stage 'app'

Write-Host "Publishing HotA MCP $version (self-contained, win-x64)..."
& dotnet publish $project -c Release -r win-x64 --self-contained true -o $app -v q --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
foreach ($required in @('HotaMcp.exe', 'native\game-attach.exe', 'native\hota_game_bridge.dll')) {
    if (-not (Test-Path (Join-Path $app $required))) { throw "Published service lacks $required" }
}
Copy-Item (Join-Path $tab 'hota_launcher_tab.dll'), (Join-Path $tab 'launcher-attach.exe') $app
# The bridge serves docs\knowledge and skills; the rest of docs is the project page and the developer guide.
foreach ($tree in @('docs\knowledge', 'skills')) {
    $destination = Join-Path $stage $tree
    New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
    Copy-Item (Join-Path $repo $tree) $destination -Recurse
}
Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $stage 'LICENSE.txt')

$output = Join-Path $dist "HotaMcp-Setup-$version.exe"
Write-Host "Building $output..."
& $Makensis /V2 "/DVERSION=$version" "/DSTAGE=$stage" "/DOUTFILE=$output" (Join-Path $PSScriptRoot 'installer\hota-mcp.nsi')
if ($LASTEXITCODE -ne 0) { throw 'makensis failed' }
$hash = (Get-FileHash $output -Algorithm SHA256).Hash
Write-Host ("Built {0} ({1:N1} MB), SHA256 {2}" -f $output, ((Get-Item $output).Length / 1MB), $hash)
