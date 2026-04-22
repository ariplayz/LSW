# LSW MVP (terminal-only)

## Components

- `lswd`: gRPC daemon over UDS (`$XDG_RUNTIME_DIR/lsw/lswd.sock`).
- `lsw`: CLI frontend.
- `lsw-qemu`: QEMU/KVM command builder for headless Windows guests.
- `lsw-lib`: shared paths, auth, proto API, and models.

## Security defaults

- Daemon token file: `$XDG_RUNTIME_DIR/lsw/auth.token` mode `0600`.
- Daemon socket: `$XDG_RUNTIME_DIR/lsw/lswd.sock` mode `0600`.
- No guest media/images are shipped; users import their own qcow2.

## Quick start

```bash
cargo build
cargo run -p lswd
# in another terminal
cargo run -p lsw-cli -- import win11 /path/to/win11.qcow2 --ssh-port 2222
cargo run -p lsw-cli -- -d win11
```
