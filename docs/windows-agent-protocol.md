# LSW Windows Agent Protocol (MVP)

This document defines the guest-agent protocol used between `lswd` (host) and `lsw-agent` (Windows guest).

## Transport

- Channel: QEMU virtio-serial port named `org.lsw.agent`.
- Development fallback: named pipe configured by `LSW_AGENT_PIPE`.
- Optional serial override: `LSW_AGENT_SERIAL_PORT` (example: `COM4`).

## Framing

Length-prefixed JSON-RPC messages:

1. 4-byte little-endian unsigned length (`u32`)
2. UTF-8 JSON payload of that exact length

Limits:

- Reject frame size `0`.
- Reject frame size greater than `16 MiB`.

## JSON-RPC

- Version: JSON-RPC 2.0
- Request shape:
  - `jsonrpc: "2.0"`
  - `id: string|number`
  - `method: string`
  - `params: object`

## Authentication and authorization

- Method `handshake({token})` must be called first.
- Privileged methods fail with `-32001` before successful handshake.
- Token rotation is supported: a new valid token replaces the prior session token and invalidates previous state.
- Token material must never be logged.

## Methods (MVP)

- `handshake({token}) -> {ok, agent_version, capabilities}`
- `ensure_ssh({public_key, username, allow_password}) -> {ok, details}`
- `mount_share({backend, tag_or_unc, guest_path, mode, username}) -> {ok, details}`
- `run_cmd_shell({shell, cmd, cwd, env}) -> {exit_code, stdout, stderr, truncated}`

ConPTY methods are present as protocol scaffolding for forward compatibility and currently return deterministic not-implemented responses:

- `open_conpty_shell`
- `conpty_write`
- `conpty_read`
- `conpty_resize`
- `conpty_close`

## Device discovery notes

- In production, use the virtio-serial device exposed by QEMU with channel `org.lsw.agent`.
- Depending on virtio drivers, this can surface as a COM port or device-backed stream.
- For bring-up and local testing, `LSW_AGENT_PIPE` can be used to connect via `\\.\pipe\<name>`.
