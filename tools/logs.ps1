<#
.SYNOPSIS
  Shows Election Atlas's game logs, and keeps following them with -Follow.

.EXAMPLE
  .\tools\logs.ps1             # last 40 lines of each log
  .\tools\logs.ps1 -Follow     # then print new lines as they arrive (Ctrl+C to stop)
#>
param(
    [int]$Lines = 40,
    [switch]$Follow
)

$logDir = Join-Path ([Environment]::GetEnvironmentVariable('CSII_USERDATAPATH', 'User')) 'Logs'
$files = 'ElectionAtlas.Mod.log', 'ElectionAtlas.Spikes.log' | ForEach-Object { Join-Path $logDir $_ }

$seen = @{}
foreach ($f in $files) {
    Write-Host "===== $(Split-Path $f -Leaf)" -ForegroundColor Cyan
    if (Test-Path $f) {
        $all = Get-Content $f
        $all | Select-Object -Last $Lines
        $seen[$f] = $all.Count
    } else {
        Write-Host "(not written yet)" -ForegroundColor DarkGray
        $seen[$f] = 0
    }
}

if (-not $Follow) { return }

Write-Host "`nFollowing (Ctrl+C to stop)..." -ForegroundColor DarkGray
while ($true) {
    foreach ($f in $files) {
        if (-not (Test-Path $f)) { continue }
        $all = @(Get-Content $f)
        if ($all.Count -lt $seen[$f]) { $seen[$f] = 0 }   # the game restarted and rewrote the log
        if ($all.Count -gt $seen[$f]) {
            $tag = if ($f -like '*Spikes*') { 'spikes' } else { 'mod' }
            $all[$seen[$f]..($all.Count - 1)] | ForEach-Object { Write-Host "[$tag] $_" }
            $seen[$f] = $all.Count
        }
    }
    Start-Sleep -Milliseconds 500
}
