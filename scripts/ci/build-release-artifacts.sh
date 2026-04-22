#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DIST_DIR="${ROOT_DIR}/dist"
TARGET_DIR="${ROOT_DIR}/target/release"
VERSION="$(awk -F ' = ' '/^version = / { gsub(/"/, "", $2); print $2; exit }' "${ROOT_DIR}/Cargo.toml")"
ARCH="x86_64"
TRIPLE="${ARCH}-unknown-linux-gnu"

rm -rf "${DIST_DIR}"
mkdir -p "${DIST_DIR}"

echo "[ci] Building release binaries..."
cargo build --release --locked -p lsw-cli -p lswd

STAGE_DIR="${DIST_DIR}/lsw-${VERSION}-${TRIPLE}"
mkdir -p "${STAGE_DIR}/bin" "${STAGE_DIR}/systemd/user"

install -m 0755 "${TARGET_DIR}/lsw-cli" "${STAGE_DIR}/bin/lsw"
install -m 0755 "${TARGET_DIR}/lswd" "${STAGE_DIR}/bin/lswd"
install -m 0644 "${ROOT_DIR}/systemd/user/lswd.service" "${STAGE_DIR}/systemd/user/lswd.service"
install -m 0644 "${ROOT_DIR}/LICENSE" "${STAGE_DIR}/LICENSE"
install -m 0644 "${ROOT_DIR}/README.md" "${STAGE_DIR}/README.md"

echo "[ci] Creating tarball..."
TAR_NAME="lsw-${VERSION}-${TRIPLE}.tar.gz"
tar -C "${DIST_DIR}" -czf "${DIST_DIR}/${TAR_NAME}" "$(basename "${STAGE_DIR}")"

echo "[ci] Building .deb and .rpm using fpm..."
fpm -s dir -t deb \
  -n lsw \
  -v "${VERSION}" \
  --architecture "${ARCH}" \
  --license "MIT" \
  --description "LSW terminal-only host stack" \
  --url "https://github.com/${GITHUB_REPOSITORY:-local/lsw}" \
  --maintainer "LSW maintainers" \
  "${TARGET_DIR}/lsw-cli=/usr/bin/lsw" \
  "${TARGET_DIR}/lswd=/usr/lib/lsw/lswd" \
  "${ROOT_DIR}/systemd/user/lswd.service=/usr/lib/systemd/user/lswd.service" \
  --package "${DIST_DIR}/lsw_${VERSION}_${ARCH}.deb"

fpm -s dir -t rpm \
  -n lsw \
  -v "${VERSION}" \
  --architecture "${ARCH}" \
  --license "MIT" \
  --description "LSW terminal-only host stack" \
  --url "https://github.com/${GITHUB_REPOSITORY:-local/lsw}" \
  --maintainer "LSW maintainers" \
  "${TARGET_DIR}/lsw-cli=/usr/bin/lsw" \
  "${TARGET_DIR}/lswd=/usr/lib/lsw/lswd" \
  "${ROOT_DIR}/systemd/user/lswd.service=/usr/lib/systemd/user/lswd.service" \
  --package "${DIST_DIR}/lsw-${VERSION}-1.${ARCH}.rpm"

echo "[ci] Release artifacts generated in ${DIST_DIR}"