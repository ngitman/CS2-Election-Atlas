<#
.SYNOPSIS
  One-command dev loop: build and deploy Election Atlas, then start Cities: Skylines II straight into the last save.

.EXAMPLE
  .\tools\dev.ps1                # build + deploy + launch (fails if the game is running)
  .\tools\dev.ps1 -Restart       # close the running game first
  .\tools\dev.ps1 -NoLaunch      # build + deploy only
  .\tools\dev.ps1 -NoBuild       # just launch
  .\tools\dev.ps1 -Flags '-continuelastsave'   # pass arguments through Steam (it asks you to allow them)
  # Usual flags go in Steam's launch options: -continuelastsave -noSplash -developerMode --uiDeveloperMode
#>
param(
    [switch]$Restart,
    [switch]$NoBuild,
    [switch]$NoLaunch,
    [switch]$SkipUI,
    # Launch options belong in Steam (Library > Cities: Skylines II > Properties > Launch Options):
    #   -continuelastsave -noSplash -developerMode --uiDeveloperMode
    # --uiDeveloperMode hot-reloads the UI and opens the UI debugger on http://localhost:9444; pair it with
    # `npm run dev` in ElectionAtlas\UI (or -Watch). -Flags passes extra arguments through Steam instead,
    # which makes Steam ask you to allow them.
    [string]$Flags = '',
    # Start the UI watcher (npm run dev) in a new window, so UI edits reach the running game.
    [switch]$Watch
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$steamAppId = 949230

$game = Get-Process -Name 'Cities2' -ErrorAction SilentlyContinue
if ($game) {
    if (-not $Restart) {
        throw "Cities: Skylines II is running and holds the mod's files. Close it, or run with -Restart."
    }
    Write-Host "Closing Cities: Skylines II..." -ForegroundColor Yellow
    $game | Stop-Process
    $game | Wait-Process -Timeout 60 -ErrorAction SilentlyContinue
}

if (-not $NoBuild) {
    Write-Host "Building and deploying (Debug, with developer tools)..." -ForegroundColor Cyan
    $buildArgs = @('build', (Join-Path $repo 'ElectionAtlas.sln'), '-c', 'Debug', '-nologo', '-v', 'q')
    if ($SkipUI) { $buildArgs += '-p:SkipUI=true' }
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
    $deploy = Join-Path ([Environment]::GetEnvironmentVariable('CSII_LOCALMODSPATH', 'User')) 'ElectionAtlas'
    Write-Host "Deployed to $deploy" -ForegroundColor Green
}

if ($Watch) {
    Write-Host "Starting the UI watcher (npm run dev)..." -ForegroundColor Cyan
    Start-Process powershell -ArgumentList '-NoExit', '-Command', "Set-Location '$(Join-Path $repo 'ElectionAtlas\UI')'; npm run dev"
}

if (-not $NoLaunch) {
    # Always go through Steam. Starting Cities2.exe directly fails with "A platform service integration
    # failed to initialize", the game never gets past GameManager.Initialize, and the Paradox Launcher opens.
    if ($Flags) {
        Write-Host "Launching through Steam with: $Flags (Steam asks you to allow the arguments)" -ForegroundColor Cyan
        Start-Process "steam://run/$steamAppId//$Flags/"
    } else {
        Write-Host "Launching through Steam with the game's Steam launch options" -ForegroundColor Cyan
        Start-Process "steam://rungameid/$steamAppId"
    }
    Write-Host "UI debugger (needs --uiDeveloperMode in the launch options): http://localhost:9444" -ForegroundColor DarkGray
}
