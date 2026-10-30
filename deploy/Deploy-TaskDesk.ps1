<#
.SYNOPSIS
    Deploys a TaskDesk release package to IIS on Windows Server, with backup, health check and automatic rollback.

.DESCRIPTION
    1. Creates the IIS application pool ("No Managed Code") and site if they do not exist.
    2. Backs up the current release folder.
    3. Takes the site offline gracefully with app_offline.htm.
    4. Mirrors the new release into the site folder (environment settings are preserved).
    5. Brings the site back online and calls /health/ready.
    6. If the health check fails, restores the previous release automatically.

    Run in an elevated PowerShell session on the web server, as part of an approved change request.

.EXAMPLE
    .\Deploy-TaskDesk.ps1 -PackagePath C:\Drops\taskdesk-1.0.0.zip

.EXAMPLE
    .\Deploy-TaskDesk.ps1 -PackagePath .\taskdesk.zip -SitePath D:\Sites\TaskDesk -HealthUrl https://taskdesk.office.local/health/ready -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string] $PackagePath,

    [string] $SiteName = 'TaskDesk',
    [string] $AppPoolName = 'TaskDeskPool',
    [string] $SitePath = 'D:\Sites\TaskDesk',
    [string] $BackupRoot = 'D:\Backups\TaskDesk',
    [int]    $Port = 80,
    [string] $HealthUrl = 'http://localhost/health/ready',
    [int]    $KeepBackups = 5
)

$ErrorActionPreference = 'Stop'
Import-Module WebAdministration

# Files that belong to the server, not to the release package.
$preservedFiles = @('appsettings.Production.json', 'app_offline.htm')
$preservedDirs = @('logs')

function Write-Step([string] $Message) {
    Write-Host ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $Message) -ForegroundColor Cyan
}

function Invoke-Mirror([string] $From, [string] $To) {
    robocopy $From $To /MIR /R:2 /W:2 /NFL /NDL /NJH /NJS /NP /XF $preservedFiles /XD $preservedDirs | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }
}

function Test-Health {
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $HealthUrl -UseBasicParsing -TimeoutSec 20
            if ($response.StatusCode -eq 200) { return $true }
        }
        catch {
            Write-Host "  health check attempt $attempt failed: $($_.Exception.Message)"
        }
        Start-Sleep -Seconds 5
    }
    return $false
}

$offlineFile = Join-Path $SitePath 'app_offline.htm'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupPath = Join-Path $BackupRoot "release-$stamp"
$stagingPath = Join-Path $env:TEMP "taskdesk-staging-$stamp"

if (-not $PSCmdlet.ShouldProcess($SiteName, "Deploy $PackagePath")) { return }

# ---- 1. Infrastructure -------------------------------------------------------------
Write-Step 'Ensuring application pool and site exist'
New-Item -ItemType Directory -Force -Path $SitePath, $BackupRoot | Out-Null

if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
    New-WebAppPool -Name $AppPoolName | Out-Null
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ''        # No Managed Code (ASP.NET Core)
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.identityType -Value 'ApplicationPoolIdentity'
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name startMode -Value 'AlwaysRunning'
    Write-Host "  created app pool $AppPoolName"
}

if (-not (Test-Path "IIS:\Sites\$SiteName")) {
    New-Website -Name $SiteName -PhysicalPath $SitePath -ApplicationPool $AppPoolName -Port $Port | Out-Null
    Write-Host "  created site $SiteName on port $Port (add the HTTPS binding + certificate separately)"
}

# ---- 2. Backup ---------------------------------------------------------------------
$hasCurrentRelease = Test-Path (Join-Path $SitePath 'TaskDesk.Web.dll')
if ($hasCurrentRelease) {
    Write-Step "Backing up current release to $backupPath"
    New-Item -ItemType Directory -Force -Path $backupPath | Out-Null
    robocopy $SitePath $backupPath /E /R:2 /W:2 /NFL /NDL /NJH /NJS /NP /XD $preservedDirs | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Backup failed (robocopy exit code $LASTEXITCODE)" }
}

# ---- 3. Stage package ----------------------------------------------------------------
Write-Step 'Extracting package'
Expand-Archive -Path $PackagePath -DestinationPath $stagingPath -Force
if (-not (Test-Path (Join-Path $stagingPath 'TaskDesk.Web.dll'))) {
    throw 'Package does not contain TaskDesk.Web.dll - wrong artifact?'
}

try {
    # ---- 4. Deploy -------------------------------------------------------------------
    Write-Step 'Taking site offline (app_offline.htm)'
    Set-Content -Path $offlineFile -Value '<!DOCTYPE html><html><body style="font-family:Segoe UI"><h2>TaskDesk is being updated</h2><p>Please try again in a minute.</p></body></html>'
    Start-Sleep -Seconds 3   # let in-flight requests finish and the worker release file locks

    Write-Step 'Copying new release'
    Invoke-Mirror -From $stagingPath -To $SitePath

    Write-Step 'Bringing site online'
    Remove-Item $offlineFile -Force

    # ---- 5. Verify -------------------------------------------------------------------
    Write-Step "Health check $HealthUrl"
    if (-not (Test-Health)) { throw 'Health check failed after deployment' }

    Write-Step 'Deployment succeeded'
}
catch {
    Write-Warning "Deployment failed: $($_.Exception.Message)"
    if ($hasCurrentRelease) {
        Write-Step 'Rolling back to previous release'
        Set-Content -Path $offlineFile -Value 'Rolling back'
        Start-Sleep -Seconds 3
        Invoke-Mirror -From $backupPath -To $SitePath
        Remove-Item $offlineFile -Force -ErrorAction SilentlyContinue
        if (Test-Health) { Write-Warning 'Rollback completed - previous release is live.' }
        else { Write-Error 'Rollback completed but the health check still fails. Escalate per docs/RUNBOOK.md.' }
    }
    throw
}
finally {
    Remove-Item $stagingPath -Recurse -Force -ErrorAction SilentlyContinue
}

# ---- 6. Housekeeping ---------------------------------------------------------------
Get-ChildItem $BackupRoot -Directory -Filter 'release-*' |
    Sort-Object Name -Descending |
    Select-Object -Skip $KeepBackups |
    Remove-Item -Recurse -Force
