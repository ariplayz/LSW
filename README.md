# LSW (Linux Subsystem for Windows)

Terminal-only, WSL-like host stack for Linux that manages per-user Windows guests via QEMU/KVM and exposes a `lsw` CLI + `lswd` daemon control plane over gRPC/UDS.

## Workspace

- `crates/lsw-lib` shared models, paths/config and protobuf-generated API glue
- `crates/lsw-qemu` safe QEMU command construction
- `crates/lswd` per-user daemon exposing control API over UDS
- `crates/lsw-cli` user CLI (`lsw` UX)
- `crates/lsw-tests` lightweight integration/unit harness
- `proto/lsw_control.proto` control plane protobuf v3 definition
- `guest/lsw-agent` Windows guest agent service (JSON-RPC over virtio-serial)

## Quickstart (Ubuntu 22.04)

1. Build:

```bash
cargo build
```

2. Start daemon (manual for MVP):

```bash
cargo run -p lswd
```

3. In another terminal, register and start a VM from existing qcow2:

```bash
cargo run -p lsw-cli -- import win11 /path/to/win11.qcow2
cargo run -p lsw-cli -- start win11
```

4. Attach (`lsw -d` equivalent):

```bash
cargo run -p lsw-cli -- -d win11
```

5. Run non-interactive command (`lsw --run` equivalent):

```bash
cargo run -p lsw-cli -- --run win11 -- powershell -c "echo hello"
```

## Systemd user service

Install unit files from `systemd/user/`, then:

```bash
systemctl --user daemon-reload
systemctl --user enable --now lswd.service
```

## Windows guest agent (Prompt 2 MVP)

- Protocol and framing: `docs/windows-agent-protocol.md`
- Build/install guide: `docs/windows-agent-install.md`
- Agent project: `guest/lsw-agent/`
- OpenRPC schema: `schemas/lsw-agent-openrpc.json`

Example host-driven agent sequence:

1. `handshake({token})`
2. `ensure_ssh({public_key, username, allow_password:false})`
3. `mount_share({backend:"smb", tag_or_unc:"\\\\10.0.2.2\\lsw_home", guest_path:"D:\\home\\alice", mode:"rw"})`
4. SSH attach via host port-forward (`lsw -d win11` flow)

## Notes

- This repository does not ship Windows media or preinstalled images.
- Bring your own Windows ISO/qcow2 and perform guest-side preparation manually (see `docs/security.md`).

## GitHub Actions release pipeline

- Workflow: `.github/workflows/release.yml`
- Trigger: manual (`workflow_dispatch`) or pushed tags matching `v*`.
- Builds and publishes:
  - Linux tarball with binaries (`lsw`, `lswd`)
  - Debian package (`.deb`)
  - RPM package (`.rpm`)
  - Arch files (`PKGBUILD`, `.SRCINFO`)

Optional AUR publishing from CI (tag runs only):

- `AUR_SSH_PRIVATE_KEY` (required)
- `AUR_PACKAGE_NAME` (optional, default: `lsw-bin`)
- `AUR_GIT_URL` (optional, default: `ssh://aur@aur.archlinux.org/<pkg>.git`)
