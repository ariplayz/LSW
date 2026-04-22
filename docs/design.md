## LSW host design (MVP)

- `lswd` is a per-user daemon exposed on UDS gRPC (`$XDG_RUNTIME_DIR/lsw/control.sock`).
- `lsw` CLI talks to daemon and drives lifecycle (`import/start/stop/status/-d/--run`).
- VM process model: one headless QEMU instance per registered VM, KVM + virtio defaults.
- Share backend policy: `virtio-fs` preferred, `9p` fallback, SMB documented as future.
- Run/attach model: guest OpenSSH server + host-local forwarded `127.0.0.1:<port> -> :22`.
