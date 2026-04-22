#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNIT_DIR="${HOME}/.config/systemd/user"

mkdir -p "${UNIT_DIR}"
cp "${ROOT_DIR}/packaging/systemd/lswd.service" "${UNIT_DIR}/lswd.service"

systemctl --user daemon-reload
systemctl --user enable --now lswd.service

echo "lswd service installed and started"
