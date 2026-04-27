# LSW Windows Guest Agent — Installation Guide

## 1. Supported Windows versions

| Version | OpenSSH built-in | ConPTY | Notes |
|---------|-----------------|--------|-------|
| Windows 11 | ✅ Usually pre-installed | ✅ | Best experience |
| Windows 10 1809+ | ✅ Optional feature | ✅ | |
| Windows 10 < 1809 | ⚠️ Must install manually | ❌ | Win32-OpenSSH |
| Tiny11 | ⚠️ May be stripped | ✅ | See §4 |
| Windows 7 | ❌ Not available | ❌ | See §5 |

---

## 2. Prerequisites

### Virtio drivers (required)

The VM must have virtio drivers for:
- **virtio-net** (network adapter)
- **virtio-storage** (disk controller, if not using IDE/SATA)
- **virtio-serial** (required for agent channel)

Download the driver ISO from:
```
https://fedorapeople.org/groups/virt/virtio-win/direct-downloads/stable-virtio/virtio-win.iso
```
Mount the ISO inside Windows and run `virtio-win-gt-x64.msi` (Windows 10/11) or the appropriate installer.

The virtio-serial driver makes the agent channel appear as `\\.\Global\org.lsw.agent`.

---

## 3. Build

On any machine with .NET 6 SDK:

```powershell
dotnet publish guest/lsw-agent/LswAgent.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -o publish/lsw-agent
```

Produces `publish/lsw-agent/LswAgent.exe` (~60 MB self-contained).

---

## 4. Install

Copy `LswAgent.exe` and `scripts/install-service.ps1` into the VM, then:

```powershell
# Run as Administrator
Set-ExecutionPolicy Bypass -Scope Process -Force
.\install-service.ps1 -BinPath "C:\Users\Administrator\Downloads\LswAgent.exe"
```

### Verify

```powershell
sc.exe query LswAgent
# SERVICE_NAME: LswAgent ... STATE: 4 RUNNING
```

---

## 5. Tiny11 notes

Tiny11 removes some optional Windows components. If `ensure_ssh` fails:

1. Check that `OpenSSH.Server` capability is available:
   ```powershell
   Get-WindowsCapability -Online -Name OpenSSH*
   ```
2. If not available, install Win32-OpenSSH from
   `https://github.com/PowerShell/Win32-OpenSSH/releases` and place it in
   `C:\Program Files\OpenSSH`.
3. Run `.\install-sshd.ps1` from the Win32-OpenSSH package.

---

## 6. Windows 7 manual preparation

Automatic `ensure_ssh` is not supported on Windows 7. Steps:

1. Install [Win32-OpenSSH](https://github.com/PowerShell/Win32-OpenSSH/releases) (the `OpenSSH-Win64.zip`).
2. Extract to `C:\Program Files\OpenSSH`.
3. Open an admin PowerShell and run:
   ```powershell
   cd "C:\Program Files\OpenSSH"
   .\install-sshd.ps1
   Start-Service sshd
   Set-Service sshd -StartupType Automatic
   ```
4. Manually copy your host public key to
   `C:\Users\Administrator\.ssh\authorized_keys` (create directories as needed).
5. Run `icacls` to set permissions:
   ```
   icacls "C:\Users\Administrator\.ssh\authorized_keys" ^
       /inheritance:r /grant:r "Administrator:(F)" /grant:r "SYSTEM:(F)"
   ```

The LSW agent service itself (for `run_cmd_shell` and `mount_share`) should still work on Windows 7 provided the virtio-serial driver is installed.

---

## 7. Uninstall

```powershell
sc.exe stop LswAgent
sc.exe delete LswAgent
Remove-Item "$env:ProgramFiles\LswAgent" -Recurse -Force
```
