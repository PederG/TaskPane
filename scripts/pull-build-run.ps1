#Requires -Version 5.1
<#
.SYNOPSIS
  Pull main, stop TaskPane if running, publish Release win-x64, then launch.

.DESCRIPTION
  Local Windows workflow companion to publish-win-x64.sh.
  Optional -AppDisplayName overrides the build-time wordmark (public default remains TaskPane).
  Do not commit private workplace names as script defaults.

.PARAMETER AppDisplayName
  Optional MSBuild AppDisplayName for this build only (e.g. a workplace wordmark).

.PARAMETER OutDir
  Publish output folder. Defaults to <repo>/artifacts/publish/win-x64.

.PARAMETER SkipPull
  Skip git checkout/pull (build and launch only).

.EXAMPLE
  .\scripts\pull-build-run.ps1

.EXAMPLE
  .\scripts\pull-build-run.ps1 -AppDisplayName YourWorkplaceName
#>
[CmdletBinding()]
param(
    [string]$AppDisplayName,
    [string]$OutDir,
    [switch]$SkipPull
)

$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutDir) {
    $OutDir = Join-Path $Root 'artifacts/publish/win-x64'
}

$Project = Join-Path $Root 'src/TaskPane.App/TaskPane.App.csproj'
$ExeName = 'TaskPane'
$ExePath = Join-Path $OutDir "$ExeName.exe"

function Stop-TaskPaneProcesses {
    $procs = @(Get-Process -Name $ExeName -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) {
        Write-Host 'TaskPane is not running.'
        return
    }

    Write-Host "Stopping $($procs.Count) TaskPane process(es)..."
    foreach ($p in $procs) {
        try {
            if ($p.CloseMainWindow()) {
                if (-not $p.WaitForExit(5000)) {
                    Write-Host "Force-stopping PID $($p.Id)..."
                    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
                }
            }
            else {
                Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
            }
        }
        catch {
            Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        }
    }

    Start-Sleep -Milliseconds 500
    $left = @(Get-Process -Name $ExeName -ErrorAction SilentlyContinue)
    if ($left.Count -gt 0) {
        Write-Host 'Force-stopping remaining TaskPane process(es)...'
        $left | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 300
    }
}

Push-Location $Root
try {
    if (-not $SkipPull) {
        Write-Host 'Checking out main and pulling...'
        git checkout main
        if ($LASTEXITCODE -ne 0) { throw "git checkout main failed ($LASTEXITCODE)" }
        git pull --ff-only
        if ($LASTEXITCODE -ne 0) { throw "git pull failed ($LASTEXITCODE)" }
    }
    else {
        Write-Host 'Skipping git pull (-SkipPull).'
    }

    Stop-TaskPaneProcesses

    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

    $publishArgs = @(
        'publish', $Project,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishReadyToRun=true',
        '-p:PublishSingleFile=false',
        '-o', $OutDir
    )
    if (-not [string]::IsNullOrWhiteSpace($AppDisplayName)) {
        $publishArgs += "-p:AppDisplayName=$AppDisplayName"
        Write-Host "Publishing with AppDisplayName=$AppDisplayName ..."
    }
    else {
        Write-Host 'Publishing (default AppDisplayName)...'
    }

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

    if (-not (Test-Path -LiteralPath $ExePath)) {
        throw "Published executable not found: $ExePath"
    }

    Write-Host "Published to $OutDir"
    Write-Host "Launching $ExePath ..."
    Start-Process -FilePath $ExePath -WorkingDirectory $OutDir
}
finally {
    Pop-Location
}
