use anyhow::{anyhow, Context, Result};
use directories::BaseDirs;
use std::path::PathBuf;

#[derive(Debug, Clone)]
pub struct RuntimePaths {
    pub runtime_dir: PathBuf,
    pub state_dir: PathBuf,
    pub socket_path: PathBuf,
    pub token_path: PathBuf,
    pub instances_dir: PathBuf,
    pub logs_dir: PathBuf,
}

impl RuntimePaths {
    pub fn discover() -> Result<Self> {
        let base = BaseDirs::new().ok_or_else(|| anyhow!("cannot discover home directory"))?;
        let runtime_root = std::env::var("XDG_RUNTIME_DIR")
            .map(PathBuf::from)
            .unwrap_or_else(|_| PathBuf::from(format!("/run/user/{}", nix::unistd::getuid())));

        let runtime_dir = runtime_root.join("lsw");
        let state_dir = base.home_dir().join(".local/share/lsw");
        let instances_dir = state_dir.join("instances");
        let logs_dir = state_dir.join("logs");

        Ok(Self {
            socket_path: runtime_dir.join("lswd.sock"),
            token_path: runtime_dir.join("auth.token"),
            runtime_dir,
            state_dir,
            instances_dir,
            logs_dir,
        })
    }

    pub async fn ensure_layout(&self) -> Result<()> {
        tokio::fs::create_dir_all(&self.runtime_dir)
            .await
            .with_context(|| format!("create {}", self.runtime_dir.display()))?;
        tokio::fs::create_dir_all(&self.instances_dir)
            .await
            .with_context(|| format!("create {}", self.instances_dir.display()))?;
        tokio::fs::create_dir_all(&self.logs_dir)
            .await
            .with_context(|| format!("create {}", self.logs_dir.display()))?;
        Ok(())
    }

    pub fn instance_config_path(&self, name: &str) -> PathBuf {
        self.instances_dir.join(format!("{name}.toml"))
    }

    pub fn instance_pid_path(&self, name: &str) -> PathBuf {
        self.instances_dir.join(format!("{name}.pid"))
    }

    pub fn instance_log_path(&self, name: &str) -> PathBuf {
        self.logs_dir.join(format!("{name}.log"))
    }
}
