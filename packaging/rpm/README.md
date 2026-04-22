Minimal RPM packaging layout for LSW MVP.

- Install `lsw` to `/usr/bin/lsw`
- Install `lswd` to `/usr/lib/lsw/lswd` (or `/usr/bin/lswd`)
- Install user unit under `/usr/lib/systemd/user/`
- Do not auto-enable user units globally; print enablement hints only.
