use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct LswConfig {
    pub defaults: Defaults,
}

impl Default for LswConfig {
    fn default() -> Self {
        Self {
            defaults: Defaults::default(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Defaults {
    pub memory_mb: u32,
    pub cpus: u32,
    pub ssh_user: String,
}

impl Default for Defaults {
    fn default() -> Self {
        Self {
            memory_mb: 4096,
            cpus: 2,
            ssh_user: "lsw".to_string(),
        }
    }
}
