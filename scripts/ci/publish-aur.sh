#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${AUR_SSH_PRIVATE_KEY:-}" ]]; then
  echo "AUR_SSH_PRIVATE_KEY is not set; skipping AUR publish" >&2
  exit 0
fi

PACKAGE_NAME="${AUR_PACKAGE_NAME:-lsw-bin}"
REPO_URL="${AUR_GIT_URL:-ssh://aur@aur.archlinux.org/${PACKAGE_NAME}.git}"

mkdir -p "$HOME/.ssh"
chmod 700 "$HOME/.ssh"
printf '%s\n' "$AUR_SSH_PRIVATE_KEY" > "$HOME/.ssh/aur"
chmod 600 "$HOME/.ssh/aur"
ssh-keyscan -H aur.archlinux.org >> "$HOME/.ssh/known_hosts"

export GIT_SSH_COMMAND="ssh -i $HOME/.ssh/aur -o StrictHostKeyChecking=yes"

WORK_DIR="$(mktemp -d)"
git clone "$REPO_URL" "$WORK_DIR"

install -m 0644 dist/arch/PKGBUILD "$WORK_DIR/PKGBUILD"
install -m 0644 dist/arch/.SRCINFO "$WORK_DIR/.SRCINFO"

git -C "$WORK_DIR" config user.name "github-actions[bot]"
git -C "$WORK_DIR" config user.email "github-actions[bot]@users.noreply.github.com"

if [[ -n "$(git -C "$WORK_DIR" status --porcelain)" ]]; then
  git -C "$WORK_DIR" add PKGBUILD .SRCINFO
  git -C "$WORK_DIR" commit -m "Update ${PACKAGE_NAME} from GitHub Actions"
  git -C "$WORK_DIR" push origin HEAD
else
  echo "AUR repo already up to date"
fi