# LSW

Linux Subsystem for Windows (terminal-only edition) implemented as a Rust workspace.

## MVP highlights

- `lswd` runs as a user daemon over gRPC + Unix domain socket.
- `lsw` CLI manages instances (`import`, `start`, `stop`, `status`) and supports `lsw -d <name>`.
- Headless QEMU/KVM launch with virtio-net, virtio-serial agent channel, and host filesystem share.
- User-supplied Windows qcow2 images (no bundled ISOs/images).
- Token-based client/daemon authentication and strict file permissions.

## Build

```bash
cargo build
cargo test
```

## User service

See `packaging/systemd/lswd.service` and `scripts/install-user-service.sh`.
