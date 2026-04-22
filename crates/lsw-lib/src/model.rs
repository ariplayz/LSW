use serde::{Deserialize, Serialize};
use std::path::PathBuf;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct InstanceConfig {
    pub name: String,
    pub disk_image: PathBuf,
    pub ssh_port: u16,
    #[serde(default = "default_memory_mb")]
    pub memory_mb: u32,
    #[serde(default = "default_cpus")]
    pub cpus: u8,
}

const fn default_memory_mb() -> u32 {
    4096
}

const fn default_cpus() -> u8 {
    2
}
