#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Install and start the LSW guest agent as a Windows service.

.DESCRIPTION
    Copies the published LswAgent binary to %ProgramFiles%\LswAgent,
    registers it as a Windows service with sc.exe, and starts it.
    Must be run as Administrator.

.EXAMPLE
    .\install-service.ps1 -BinPath "C:\build\LswAgent.exe"
#>
param(
    [Parameter(Mandatory)]
    [string]$BinPath,

    [string]$ServiceName = "LswAgent",
    [string]$InstallDir  = "$env:ProgramFiles\LswAgent",
    [string]$DisplayName = "LSW Guest Agent",
    [string]$Description = "LSW guest agent: virtio-serial JSON-RPC, SSH setup, host share mount"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---- validate ----------------------------------------------------------------

if (-not (Test-Path $BinPath)) {
    Write-Error "Binary not found: $BinPath"
    exit 1
}

# ---- install directory -------------------------------------------------------

if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir | Out-Null
    Write-Host "Created $InstallDir"
}

$destExe = Join-Path $InstallDir "LswAgent.exe"
Copy-Item -Path $BinPath -Destination $destExe -Force
Write-Host "Copied binary -> $destExe"

# Restrict permissions: only SYSTEM + Administrators can read/execute
icacls $destExe /inheritance:r /grant:r "SYSTEM:(RX)" /grant:r "Administrators:(RX)" | Out-Null

# ---- register service --------------------------------------------------------

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Service '$ServiceName' exists, removing..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

sc.exe create $ServiceName `
    binPath= "`"$destExe`"" `
    start= auto `
    DisplayName= $DisplayName | Out-Null

sc.exe description $ServiceName $Description | Out-Null

# Run as LocalSystem (default) — least privilege for an admin agent.
# If you want to run as a restricted account, change the service account here.

# ---- create Event Log source (optional, ignore failure on Server Core) -------
try {
    if (-not ([System.Diagnostics.EventLog]::SourceExists("LswAgent"))) {
        New-EventLog -LogName Application -Source "LswAgent"
        Write-Host "Created Event Log source"
    }
} catch {
    Write-Warning "Could not create Event Log source: $_"
}

# ---- start service -----------------------------------------------------------

Start-Service -Name $ServiceName
$svc = Get-Service -Name $ServiceName
Write-Host "Service '$ServiceName' status: $($svc.Status)"

Write-Host ""
Write-Host "LSW Agent installed successfully."
Write-Host "  Binary:  $destExe"
Write-Host "  Service: $ServiceName (auto-start)"
Write-Host ""
Write-Host "To verify: sc.exe query $ServiceName"
Write-Host "To uninstall: sc.exe stop $ServiceName ; sc.exe delete $ServiceName"
