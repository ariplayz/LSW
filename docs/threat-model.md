## Threat model summary

- Local user boundary: only the owning UID should control the daemon.
- Guest compromise: treat guest as potentially hostile; restrict host exposure and default shares.
- Secret exposure: key/token material under `$XDG_STATE_HOME/lsw/` with strict permissions.
- Network exposure: no default bind to `0.0.0.0`; SSH forwarded to loopback only.
