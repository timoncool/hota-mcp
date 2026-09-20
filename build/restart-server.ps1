param([int]$LauncherPid,[string]$Exe,[string]$WorkDir)
$ErrorActionPreference='Stop'
try{
  $p=[IO.Pipes.NamedPipeClientStream]::new('.',("hota-mcp-control-{0}" -f $LauncherPid),[IO.Pipes.PipeDirection]::InOut)
  $p.Connect(1000)
  $b=[Text.Encoding]::UTF8.GetBytes('stop'); $p.Write($b,0,$b.Length)
  $buf=[byte[]]::new(512); $null=$p.Read($buf,0,512); $p.Dispose()
  Write-Output ("stop-reply: "+[Text.Encoding]::UTF8.GetString($buf).Trim([char]0))
}catch{ Write-Output ("stop-failed: "+$_.Exception.Message) }
Start-Sleep -Seconds 2
Start-Process -FilePath $Exe -ArgumentList '--launcher-pid',"$LauncherPid" -WorkingDirectory $WorkDir -WindowStyle Hidden
Start-Sleep -Seconds 3
Get-Process HotaMcp -ErrorAction SilentlyContinue | Select-Object Id,ProcessName,StartTime | Format-Table -AutoSize | Out-String -Width 200
