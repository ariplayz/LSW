use std::fs;
use std::path::PathBuf;

use anyhow::{Context, Result};

#[derive(Debug, Clone)]
pub struct LswPaths {
    pub config_home: PathBuf,
    pub data_home: PathBuf,
    pub state_home: PathBuf,
    pub runtime_home: PathBuf,
}

impl LswPaths {
    pub fn discover() -> Result<Self> {
        let home = dirs::home_dir().context("failed to resolve home dir")?;
        let config_home = std::env::var_os("XDG_CONFIG_HOME")
            .map(PathBuf::from)
            .unwrap_or_else(|| home.join(".config"))
            .join("lsw");
        let data_home = std::env::var_os("XDG_DATA_HOME")
            .map(PathBuf::from)
            .unwrap_or_else(|| home.join(".local/share"))
            .join("lsw");
        let state_home = std::env::var_os("XDG_STATE_HOME")
            .map(PathBuf::from)
            .unwrap_or_else(|| home.join(".local/state"))
            .join("lsw");
        let runtime_base = std::env::var_os("XDG_RUNTIME_DIR")
            .map(PathBuf::from)
            .unwrap_or_else(|| PathBuf::from(format!("/run/user/{}", nix_uid())));
        let runtime_home = runtime_base.join("lsw");

        Ok(Self {
            config_home,
            data_home,
            state_home,
            runtime_home,
        })
    }

    pub fn control_socket(&self) -> PathBuf {
        self.runtime_home.join(super::CONTROL_SOCK)
    }

    pub fn ensure_dirs(&self) -> Result<()> {
        ensure_dir(&self.config_home, 0o700)?;
        ensure_dir(&self.data_home, 0o700)?;
        ensure_dir(&self.state_home, 0o700)?;
        ensure_dir(&self.runtime_home, 0o700)?;
        ensure_dir(&self.state_home.join("logs"), 0o700)?;
        ensure_dir(&self.state_home.join("keys"), 0o700)?;
        ensure_dir(&self.state_home.join("tokens"), 0o700)?;
        Ok(())
    }
}

fn ensure_dir(path: &PathBuf, mode: u32) -> Result<()> {
    fs::create_dir_all(path).with_context(|| format!("failed to create dir {}", path.display()))?;
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        fs::set_permissions(path, fs::Permissions::from_mode(mode)).with_context(|| {
            format!("failed to set permissions on {} to {:o}", path.display(), mode)
        })?;
    }
    Ok(())
}

fn nix_uid() -> u32 {
    nix::unistd::Uid::current().as_raw()
}
