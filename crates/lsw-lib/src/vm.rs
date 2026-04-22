use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "snake_case")]
pub enum VmState {
    Stopped,
    Starting,
    Running,
    Paused,
    Stopping,
    Error,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct VmShare {
    pub host_path: String,
    pub guest_path: String,
    pub mode: String,
    pub backend_preference: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct VmRecord {
    pub id: String,
    pub name: String,
    pub created_at: DateTime<Utc>,
    pub state: VmState,
    pub disk_path: String,
    pub memory_mb: u32,
    pub cpus: u32,
    pub ssh_forward_port: u32,
    pub default_vm: bool,
    pub shares: Vec<VmShare>,
    pub ssh_user: String,
}
