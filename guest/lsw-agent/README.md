# LSW Guest Agent

Windows service that enables the WSL-like UX for LSW.
It runs inside a Windows 10/11 (or Tiny11) QEMU/KVM VM and communicates with the Linux host daemon (`lswd`) over a QEMU virtio-serial channel.

---

## Prerequisites

| Requirement | Minimum |
|---|---|
| Windows | 10 1809 (build 17763) for full features; 10 1507+ for SSH-only |
| .NET runtime | .NET 6 (or use self-contained publish) |
| Virtio drivers | `virtio-serial` (included in [virtio-win ISO](https://fedorapeople.org/groups/virt/virtio-win/direct-downloads/)) |
| Admin rights | Required for service install and SSH configuration |

---

## Build

```powershell
# From repo root on any platform with .NET 6 SDK
dotnet publish guest/lsw-agent/LswAgent.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -o publish/lsw-agent
# Output: publish/lsw-agent/LswAgent.exe
```

---

## Install (inside the Windows VM)

Copy `LswAgent.exe` to the VM, then open an **Administrator** PowerShell:

```powershell
Set-ExecutionPolicy Bypass -Scope Process -Force
.\scripts\install-service.ps1 -BinPath "C:\path\to\LswAgent.exe"
```

This:
1. Copies the binary to `%ProgramFiles%\LswAgent\LswAgent.exe`
2. Sets restricted file ACLs (SYSTEM + Administrators only)
3. Registers `LswAgent` as an auto-start Windows service
4. Creates an Event Log source
5. Starts the service immediately

### Verify

```powershell
sc.exe query LswAgent
# Expected: STATE: 4 RUNNING
```

---

## Virtio-serial device name

The agent listens on the virtio-serial port named `org.lsw.agent`.

On Windows with the virtio-win driver, this appears as a named pipe:

```
\\.\Global\org.lsw.agent
```

If your driver exposes it as a COM port instead, set:

```
[Environment]::SetEnvironmentVariable("LSW_SERIAL_PORT", "COM3", "Machine")
```

and restart the service.

---

## Uninstall

```powershell
sc.exe stop LswAgent
sc.exe delete LswAgent
Remove-Item "$env:ProgramFiles\LswAgent" -Recurse -Force
```

---

## Windows 7 notes

OpenSSH built-in capability is **not available** on Windows 7.
You must install [Win32-OpenSSH](https://github.com/PowerShell/Win32-OpenSSH/releases) manually before `ensure_ssh` will succeed.

ConPTY is not available on Windows 7. Interactive sessions via `lsw -d` must rely on the SSH server's own PTY handling.

---

## Logging

- **Console**: visible when running interactively (not as a service)
- **Windows Event Log** → Application → Source: `LswAgent` (Windows 10+ only)
- Set environment variable `LOGGING__LOGLEVEL__DEFAULT=Debug` for verbose logs
