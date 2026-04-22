# lsw-agent (Windows guest agent)

`lsw-agent` is the Windows guest-side service for LSW. It exposes JSON-RPC 2.0 over the QEMU `org.lsw.agent` virtio channel and performs handshake-gated actions: SSH bootstrap, share mounting, and non-interactive command execution.

## Build

```powershell
dotnet restore .\guest\lsw-agent\lsw-agent.csproj
dotnet publish .\guest\lsw-agent\lsw-agent.csproj -c Release -r win-x64 --self-contained false
```

## Install as service

From an elevated PowerShell terminal:

```powershell
powershell -ExecutionPolicy Bypass -File .\guest\lsw-agent\scripts\install-service.ps1
```

Optional overrides:

- `-BinaryPath C:\Program Files\LSW\lsw-agent\lsw-agent.exe`
- `-ServiceName LswAgent`
- Environment variables:
  - `LSW_AGENT_SERIAL_PORT` (e.g., `COM4`) for serial transport override.
  - `LSW_AGENT_PIPE` for named-pipe fallback mode during development.

## Verify

```powershell
Get-Service LswAgent
Get-WinEvent -LogName Application | Where-Object {$_.ProviderName -eq 'LswAgent'} | Select-Object -First 20
```

Then run host-side flow:

1. `handshake({token})`
2. `ensure_ssh({public_key, username, allow_password:false})`
3. `mount_share({backend:"smb", tag_or_unc:"\\\\10.0.2.2\\lsw_home", guest_path:"D:\\home\\alice", mode:"rw"})`
4. `run_cmd_shell({shell:"powershell", cmd:"Get-Location", cwd:"D:\\home\\alice"})`

## Notes

- Win10/11/Tiny11: tries to enable OpenSSH server via Windows Capabilities.
- Win7: automated OpenSSH setup is not supported; manual prep is required.
- ConPTY JSON-RPC methods are present as protocol scaffolding and currently return deterministic "not implemented" for MVP.
