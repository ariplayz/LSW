#!/usr/bin/env bash
set -euo pipefail

systemctl --user daemon-reload
systemctl --user enable --now lswd.service

echo "LSW daemon enabled for user: $(id -un)"
