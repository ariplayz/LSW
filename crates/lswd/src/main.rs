use anyhow::{anyhow, Context, Result};
use lsw_lib::auth::ensure_token_file;
use lsw_lib::model::InstanceConfig;
use lsw_lib::paths::RuntimePaths;
use lsw_lib::proto::control_plane_server::{ControlPlane, ControlPlaneServer};
use lsw_lib::proto::{
    ImportImageRequest, ImportImageResponse, StartRequest, StartResponse, StatusRequest, StatusResponse,
    StopRequest, StopResponse,
};
use lsw_qemu::{build_qemu_command, is_pid_running, read_pid, QemuVmConfig};
use std::os::unix::fs::PermissionsExt;
use std::path::Path;
use std::process::Stdio;
use tokio::net::UnixListener;
use tokio_stream::wrappers::UnixListenerStream;
use tonic::transport::Server;
use tonic::{metadata::MetadataMap, Request, Response, Status};
use tracing::{error, info};

#[derive(Clone)]
struct Daemon {
    token: String,
    paths: RuntimePaths,
}

impl Daemon {
    fn authorize(&self, md: &MetadataMap) -> Result<(), Status> {
        let supplied = md
            .get("x-lsw-token")
            .and_then(|v| v.to_str().ok())
            .ok_or_else(|| Status::unauthenticated("missing auth token"))?;

        if supplied != self.token {
            return Err(Status::permission_denied("invalid auth token"));
        }

        Ok(())
    }

    async fn load_instance(&self, name: &str) -> Result<InstanceConfig, Status> {
        let path = self.paths.instance_config_path(name);
        let content = tokio::fs::read_to_string(&path)
            .await
            .map_err(|_| Status::not_found("instance not imported"))?;
        toml::from_str::<InstanceConfig>(&content)
            .map_err(|e| Status::internal(format!("invalid config: {e}")))
    }

    async fn save_instance(&self, cfg: &InstanceConfig) -> Result<(), Status> {
        let path = self.paths.instance_config_path(&cfg.name);
        let body = toml::to_string_pretty(cfg)
            .map_err(|e| Status::internal(format!("serialize config: {e}")))?;
        tokio::fs::write(&path, body)
            .await
            .map_err(|e| Status::internal(format!("write config: {e}")))
    }

    fn running_state(&self, name: &str) -> bool {
        let pid_path = self.paths.instance_pid_path(name);
        if !pid_path.exists() {
            return false;
        }
        match read_pid(&pid_path) {
            Ok(pid) => is_pid_running(pid),
            Err(_) => false,
        }
    }
}

#[tonic::async_trait]
impl ControlPlane for Daemon {
    async fn import_image(
        &self,
        request: Request<ImportImageRequest>,
    ) -> Result<Response<ImportImageResponse>, Status> {
        self.authorize(request.metadata())?;
        let req = request.into_inner();
        if req.name.trim().is_empty() {
            return Err(Status::invalid_argument("name required"));
        }
        let disk = Path::new(&req.qcow2_path);
        if !disk.exists() {
            return Err(Status::invalid_argument("qcow2 path does not exist"));
        }

        let cfg = InstanceConfig {
            name: req.name,
            disk_image: disk.to_path_buf(),
            ssh_port: if req.ssh_port == 0 { 2222 } else { req.ssh_port as u16 },
            memory_mb: 4096,
            cpus: 2,
        };

        self.save_instance(&cfg).await?;

        Ok(Response::new(ImportImageResponse {
            name: cfg.name,
            disk_image: cfg.disk_image.display().to_string(),
            ssh_port: cfg.ssh_port as u32,
        }))
    }

    async fn start(&self, request: Request<StartRequest>) -> Result<Response<StartResponse>, Status> {
        self.authorize(request.metadata())?;
        let req = request.into_inner();
        let cfg = self.load_instance(&req.name).await?;

        if self.running_state(&cfg.name) {
            return Ok(Response::new(StartResponse {
                name: cfg.name,
                started: false,
                ssh_port: cfg.ssh_port as u32,
                mount_path: format!(r"D:\\home\\{}\\", whoami::username()),
            }));
        }

        let pid_file = self.paths.instance_pid_path(&cfg.name);
        let log_file = self.paths.instance_log_path(&cfg.name);
        let agent_socket = self.paths.runtime_dir.join(format!("{}.agent.sock", cfg.name));
        let home = directories::BaseDirs::new()
            .ok_or_else(|| Status::internal("no home dir"))?
            .home_dir()
            .to_path_buf();

        let vm_cfg = QemuVmConfig {
            disk_image: &cfg.disk_image,
            host_share: &home,
            pid_file: &pid_file,
            log_file: &log_file,
            agent_socket: &agent_socket,
            ssh_port: cfg.ssh_port,
            memory_mb: cfg.memory_mb,
            cpus: cfg.cpus,
        };

        let mut cmd = build_qemu_command(&vm_cfg);
        let log = std::fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(&log_file)
            .map_err(|e| Status::internal(format!("open log: {e}")))?;
        let log2 = log
            .try_clone()
            .map_err(|e| Status::internal(format!("clone log fd: {e}")))?;
        cmd.stdout(Stdio::from(log));
        cmd.stderr(Stdio::from(log2));

        cmd.spawn()
            .map_err(|e| Status::internal(format!("spawn qemu: {e}")))?;

        Ok(Response::new(StartResponse {
            name: cfg.name,
            started: true,
            ssh_port: cfg.ssh_port as u32,
            mount_path: format!(r"D:\\home\\{}\\", whoami::username()),
        }))
    }

    async fn stop(&self, request: Request<StopRequest>) -> Result<Response<StopResponse>, Status> {
        self.authorize(request.metadata())?;
        let req = request.into_inner();

        let pid_file = self.paths.instance_pid_path(&req.name);
        if !pid_file.exists() {
            return Ok(Response::new(StopResponse {
                name: req.name,
                stopped: false,
            }));
        }

        let pid = read_pid(&pid_file).map_err(|e| Status::internal(format!("read pid: {e}")))?;
        nix::sys::signal::kill(nix::unistd::Pid::from_raw(pid), nix::sys::signal::Signal::SIGTERM)
            .map_err(|e| Status::internal(format!("stop vm: {e}")))?;

        let _ = tokio::fs::remove_file(pid_file).await;

        Ok(Response::new(StopResponse {
            name: req.name,
            stopped: true,
        }))
    }

    async fn status(
        &self,
        request: Request<StatusRequest>,
    ) -> Result<Response<StatusResponse>, Status> {
        self.authorize(request.metadata())?;
        let req = request.into_inner();
        let path = self.paths.instance_config_path(&req.name);

        if !path.exists() {
            return Ok(Response::new(StatusResponse {
                name: req.name,
                imported: false,
                running: false,
                disk_image: String::new(),
                ssh_port: 0,
            }));
        }

        let cfg = self.load_instance(&req.name).await?;

        Ok(Response::new(StatusResponse {
            name: cfg.name.clone(),
            imported: true,
            running: self.running_state(&cfg.name),
            disk_image: cfg.disk_image.display().to_string(),
            ssh_port: cfg.ssh_port as u32,
        }))
    }
}

#[tokio::main]
async fn main() -> Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(tracing_subscriber::EnvFilter::from_default_env())
        .init();

    let paths = RuntimePaths::discover()?;
    paths.ensure_layout().await?;

    let token = ensure_token_file(&paths.token_path).await?;

    if paths.socket_path.exists() {
        tokio::fs::remove_file(&paths.socket_path)
            .await
            .with_context(|| format!("remove stale {}", paths.socket_path.display()))?;
    }

    let listener = UnixListener::bind(&paths.socket_path)
        .with_context(|| format!("bind socket {}", paths.socket_path.display()))?;
    tokio::fs::set_permissions(&paths.socket_path, std::fs::Permissions::from_mode(0o600)).await?;

    let svc = Daemon {
        token,
        paths: paths.clone(),
    };

    info!(socket = %paths.socket_path.display(), "lswd started");

    if let Err(err) = Server::builder()
        .add_service(ControlPlaneServer::new(svc))
        .serve_with_incoming(UnixListenerStream::new(listener))
        .await
    {
        error!(error = %err, "server failed");
        return Err(anyhow!(err));
    }

    Ok(())
}
