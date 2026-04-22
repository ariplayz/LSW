param(
    [string]$ServiceName = "LswAgent",
    [string]$BinaryPath = "C:\Program Files\LSW\lsw-agent\lsw-agent.exe"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $BinaryPath)) {
    throw "Binary not found: $BinaryPath"
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Service '$ServiceName' already exists; restarting with current config."
    Restart-Service -Name $ServiceName -Force
    exit 0
}

New-Service -Name $ServiceName -BinaryPathName "`"$BinaryPath`"" -DisplayName "LSW Guest Agent" -Description "LSW Windows guest control agent" -StartupType Automatic
Start-Service -Name $ServiceName
Write-Host "Service '$ServiceName' installed and started."
