# Windows Guest Agent Installation (LSW)

## Prerequisites

- Windows 10/11/Tiny11 VM prepared by the user.
- Virtio storage/network drivers installed in guest.
- .NET 6 runtime present in guest (or publish self-contained).
- Administrative PowerShell for service installation.

## Build and publish

On a system with .NET SDK:

```powershell
dotnet publish .\guest\lsw-agent\lsw-agent.csproj -c Release -r win-x64 --self-contained false -o .\artifacts\lsw-agent
```

Copy publish output to guest, e.g.:

`C:\Program Files\LSW\lsw-agent\`

## Register service

In elevated PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\guest\lsw-agent\scripts\install-service.ps1 -BinaryPath "C:\Program Files\LSW\lsw-agent\lsw-agent.exe"
```

## Verify

```powershell
Get-Service LswAgent
sc.exe qc LswAgent
```

Expected:

- Startup type `AUTO_START`
- Service state `Running`

## Manual compatibility notes

- **Windows 11/10/Tiny11**: agent attempts to enable OpenSSH Server capability in `ensure_ssh`.
- **Tiny11 caveat**: OpenSSH capability can be removed in some images; install feature manually before relying on automation.
- **Windows 7**: no built-in OpenSSH capability. Use manual Win32-OpenSSH installation and manual SSH key setup.

## Host/guest handshake flow (MVP)

1. Host writes JSON-RPC `handshake` with per-VM token over `org.lsw.agent`.
2. Host calls `ensure_ssh` with public key + username.
3. Host calls `mount_share` with SMB UNC path for `D:\home\<username>`.
4. Host opens SSH session through QEMU port forward to guest port `22`.
