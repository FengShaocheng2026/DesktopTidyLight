param([ValidatePattern('^[a-zA-Z0-9-]+$')][string]$Label='current',[ValidateRange(1,3600)][int]$Seconds=30)
$ErrorActionPreference = 'Stop'
$output = Join-Path (Split-Path $PSScriptRoot -Parent) 'build/review'
New-Item -ItemType Directory -Force -Path $output | Out-Null
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class GuiResources { [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr process,uint flag); }'
$proc=Get-Process DesktopTidyLight | Select-Object -First 1
$cpu=$proc.TotalProcessorTime.TotalMilliseconds
$watch=[Diagnostics.Stopwatch]::StartNew()
$rows=@()
for($i=0;$i -lt $Seconds;$i++) {
 Start-Sleep -Milliseconds 1000
 $proc.Refresh()
 $rows += [pscustomobject]@{Seconds=$watch.Elapsed.TotalSeconds;WorkingMB=$proc.WorkingSet64/1MB;PrivateMB=$proc.PrivateMemorySize64/1MB;Handles=$proc.HandleCount;Gdi=[GuiResources]::GetGuiResources($proc.Handle,0);User=[GuiResources]::GetGuiResources($proc.Handle,1)}
}
$ms=$proc.TotalProcessorTime.TotalMilliseconds-$cpu
$result=[pscustomobject]@{Label=$Label;DurationSeconds=$watch.Elapsed.TotalSeconds;CpuMilliseconds=$ms;OneCorePercent=100*$ms/$watch.Elapsed.TotalMilliseconds;MachinePercent=100*$ms/$watch.Elapsed.TotalMilliseconds/[Environment]::ProcessorCount;Processors=[Environment]::ProcessorCount;Samples=$rows}
$result | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $output "idle-$Label.json")
$result | Select-Object Label,DurationSeconds,CpuMilliseconds,OneCorePercent,MachinePercent,Processors | Format-List
$rows | Select-Object -First 1
$rows | Select-Object -Last 1
