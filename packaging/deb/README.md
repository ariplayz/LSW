Minimal Debian packaging layout for LSW MVP.

- Install `lsw` to `/usr/bin/lsw`
- Install `lswd` to `/usr/lib/lsw/lswd`
- Install user unit to `/usr/lib/systemd/user/lswd.service`
- Post-install should print hint: `systemctl --user enable --now lswd.service`
