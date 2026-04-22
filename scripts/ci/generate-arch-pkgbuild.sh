#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 5 ]]; then
  echo "usage: $0 <version> <repo_owner> <repo_name> <tarball_name> <output_dir>" >&2
  exit 1
fi

VERSION="$1"
REPO_OWNER="$2"
REPO_NAME="$3"
TARBALL_NAME="$4"
OUTPUT_DIR="$5"
PKGNAME="lsw-bin"
PKGREL="1"
ARCH="x86_64"
SOURCE_URL="https://github.com/${REPO_OWNER}/${REPO_NAME}/releases/download/v${VERSION}/${TARBALL_NAME}"

mkdir -p "${OUTPUT_DIR}"

SHA256="$(sha256sum "dist/${TARBALL_NAME}" | awk '{print $1}')"

cat > "${OUTPUT_DIR}/PKGBUILD" <<EOF
pkgname=${PKGNAME}
pkgver=${VERSION}
pkgrel=${PKGREL}
pkgdesc="Linux Subsystem for Windows (terminal-only host stack)"
arch=('${ARCH}')
url="https://github.com/${REPO_OWNER}/${REPO_NAME}"
license=('MIT')
depends=('openssh' 'qemu-system-x86')
source=("${SOURCE_URL}")
sha256sums=('${SHA256}')

package() {
  install -Dm755 "\${srcdir}/lsw-${VERSION}-${ARCH}-unknown-linux-gnu/bin/lsw" "\${pkgdir}/usr/bin/lsw"
  install -Dm755 "\${srcdir}/lsw-${VERSION}-${ARCH}-unknown-linux-gnu/bin/lswd" "\${pkgdir}/usr/lib/lsw/lswd"
  install -Dm644 "\${srcdir}/lsw-${VERSION}-${ARCH}-unknown-linux-gnu/systemd/user/lswd.service" "\${pkgdir}/usr/lib/systemd/user/lswd.service"
  install -Dm644 "\${srcdir}/lsw-${VERSION}-${ARCH}-unknown-linux-gnu/LICENSE" "\${pkgdir}/usr/share/licenses/${PKGNAME}/LICENSE"
}
EOF

cat > "${OUTPUT_DIR}/.SRCINFO" <<EOF
pkgbase = ${PKGNAME}
  pkgdesc = Linux Subsystem for Windows (terminal-only host stack)
  pkgver = ${VERSION}
  pkgrel = ${PKGREL}
  url = https://github.com/${REPO_OWNER}/${REPO_NAME}
  arch = ${ARCH}
  license = MIT
  depends = openssh
  depends = qemu-system-x86
  source = ${SOURCE_URL}
  sha256sums = ${SHA256}

pkgname = ${PKGNAME}
EOF

echo "Generated PKGBUILD and .SRCINFO at ${OUTPUT_DIR}"