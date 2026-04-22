Arch packaging automation for LSW release pipeline.

- PKGBUILD and `.SRCINFO` are generated in CI under `dist/arch/`.
- Default package target is `lsw-bin` (binary package from GitHub release tarball).
- AUR publishing is optional and gated by repository secrets:
  - `AUR_SSH_PRIVATE_KEY`
  - `AUR_PACKAGE_NAME` (optional, defaults to `lsw-bin`)
  - `AUR_GIT_URL` (optional, defaults to `ssh://aur@aur.archlinux.org/<pkg>.git`)