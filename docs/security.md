## Security and threat model (MVP)

- Daemon is per-user and listens only on UDS: `$XDG_RUNTIME_DIR/lsw/control.sock`.
- Runtime/state dirs are created with `0700`; key/token files are expected `0600`.
- Network defaults to QEMU user-mode NAT and hostfwd bound to `127.0.0.1` only.
- File sharing is explicit and least-privilege by default; SMB fallback is not implemented in MVP.

## Manual Windows preparation

- Install OpenSSH server in guest and configure key auth for user account.
- Install required virtio drivers/tools as appropriate for your guest image.
- Configure guest startup so `D:\home\<username>` maps to host share mount location.
- Optional: install guest agent corresponding to `org.lsw.agent` virtio-serial channel.

## Caveats

- This MVP does not ship Windows media, unattended installers, or proprietary drivers.
- Snapshot/export operations are currently task placeholders in daemon API.
