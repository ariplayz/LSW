use std::collections::HashMap;
use std::net::TcpListener;
use std::pin::Pin;
use std::sync::Arc;

use anyhow::{Context, Result};
use chrono::Utc;
use futures::Stream;
use lsw_lib::paths::LswPaths;
use lsw_lib::proto::lsw_control_server::{LswControl, LswControlServer};
use lsw_lib::proto::*;
use lsw_lib::vm::{VmRecord, VmShare, VmState};
use tokio::sync::RwLock;
use tokio_stream::wrappers::UnixListenerStream;
use tonic::{Request, Response, Status};
use tracing::info;
use uuid::Uuid;

#[derive(Clone)]
struct Daemon {
    paths: LswPaths,
    started_unix: u64,
    vms: Arc<RwLock<HashMap<String, VmRecord>>>,
}

#[tokio::main]
async fn main() -> Result<()> {
    let paths = LswPaths::discover()?;
    paths.ensure_dirs()?;
    setup_logging(&paths)?;

    let sock = paths.control_socket();
    if sock.exists() {
        std::fs::remove_file(&sock)?;
    }
    let listener = tokio::net::UnixListener::bind(&sock)
        .with_context(|| format!("failed to bind {}", sock.display()))?;

    info!("lswd listening on {}", sock.display());

    tonic::transport::Server::builder()
        .add_service(LswControlServer::new(Daemon {
            paths,
            started_unix: Utc::now().timestamp() as u64,
            vms: Arc::new(RwLock::new(HashMap::new())),
        }))
        .serve_with_incoming(UnixListenerStream::new(listener))
        .await
        .context("daemon server failed")?;
    Ok(())
}

fn setup_logging(paths: &LswPaths) -> Result<()> {
    let log_path = paths.state_home.join("logs/lswd.log.json");
    let file = std::fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(log_path)?;
    tracing_subscriber::fmt()
        .json()
        .with_writer(std::sync::Mutex::new(file))
        .with_env_filter("info")
        .init();
    Ok(())
}

impl Daemon {
    fn to_proto(vm: &VmRecord) -> VmInfo {
        VmInfo {
            id: vm.id.clone(),
            name: vm.name.clone(),
            state: format!("{:?}", vm.state).to_lowercase(),
            memory_mb: vm.memory_mb,
            cpus: vm.cpus,
            disk_path: vm.disk_path.clone(),
            ssh_forward_port: vm.ssh_forward_port,
            is_default: vm.default_vm,
            shares: vm
                .shares
                .iter()
                .map(|s| Share {
                    host_path: s.host_path.clone(),
                    guest_path: s.guest_path.clone(),
                    mode: s.mode.clone(),
                    backend_preference: s.backend_preference.clone(),
                })
                .collect(),
        }
    }

    fn alloc_port() -> Result<u32> {
        let listener = TcpListener::bind("127.0.0.1:0")?;
        Ok(listener.local_addr()?.port() as u32)
    }
}

type RunStream = Pin<Box<dyn Stream<Item = Result<RunCommandChunk, Status>> + Send>>;

#[tonic::async_trait]
impl LswControl for Daemon {
    type RunCommandInVMStream = RunStream;

    async fn list_v_ms(&self, _: Request<ListVMsRequest>) -> Result<Response<ListVMsResponse>, Status> {
        let vms = self.vms.read().await;
        Ok(Response::new(ListVMsResponse {
            vms: vms.values().map(Self::to_proto).collect(),
        }))
    }

    async fn create_vm(&self, request: Request<CreateVmRequest>) -> Result<Response<CreateVmResponse>, Status> {
        let req = request.into_inner();
        let vm = VmRecord {
            id: Uuid::new_v4().to_string(),
            name: req.name,
            created_at: Utc::now(),
            state: VmState::Stopped,
            disk_path: String::new(),
            memory_mb: req.memory_mb,
            cpus: req.cpus,
            ssh_forward_port: Self::alloc_port().map_err(internal_err)?,
            default_vm: false,
            shares: vec![],
            ssh_user: "lsw".to_string(),
        };
        let mut vms = self.vms.write().await;
        vms.insert(vm.name.clone(), vm.clone());
        Ok(Response::new(CreateVmResponse { vm: Some(Self::to_proto(&vm)) }))
    }

    async fn import_vm(&self, request: Request<ImportVmRequest>) -> Result<Response<ImportVmResponse>, Status> {
        let req = request.into_inner();
        let home = std::env::var("HOME").unwrap_or_else(|_| "/tmp".to_string());
        let vm = VmRecord {
            id: Uuid::new_v4().to_string(),
            name: req.name,
            created_at: Utc::now(),
            state: VmState::Stopped,
            disk_path: req.qcow2_path,
            memory_mb: 4096,
            cpus: 2,
            ssh_forward_port: Self::alloc_port().map_err(internal_err)?,
            default_vm: false,
            shares: vec![VmShare {
                host_path: home,
                guest_path: "D:\\home\\user".to_string(),
                mode: "rw".to_string(),
                backend_preference: "virtiofs".to_string(),
            }],
            ssh_user: "lsw".to_string(),
        };
        let mut vms = self.vms.write().await;
        vms.insert(vm.name.clone(), vm.clone());
        Ok(Response::new(ImportVmResponse { vm: Some(Self::to_proto(&vm)) }))
    }

    async fn export_vm(&self, _: Request<ExportVmRequest>) -> Result<Response<ExportVmResponse>, Status> {
        Ok(Response::new(ExportVmResponse {
            task_id: Uuid::new_v4().to_string(),
        }))
    }

    async fn start_vm(&self, request: Request<StartVmRequest>) -> Result<Response<StartVmResponse>, Status> {
        let req = request.into_inner();
        let mut vms = self.vms.write().await;
        let vm = vms.get_mut(&req.name).ok_or_else(|| Status::not_found("vm not found"))?;
        vm.state = VmState::Running;
        Ok(Response::new(StartVmResponse { vm: Some(Self::to_proto(vm)) }))
    }

    async fn stop_vm(&self, request: Request<StopVmRequest>) -> Result<Response<StopVmResponse>, Status> {
        let req = request.into_inner();
        let mut vms = self.vms.write().await;
        let vm = vms.get_mut(&req.name).ok_or_else(|| Status::not_found("vm not found"))?;
        vm.state = VmState::Stopped;
        Ok(Response::new(StopVmResponse { vm: Some(Self::to_proto(vm)) }))
    }

    async fn terminate_vm(&self, request: Request<TerminateVmRequest>) -> Result<Response<TerminateVmResponse>, Status> {
        self.stop_vm(Request::new(StopVmRequest { name: request.into_inner().name }))
            .await
            .map(|r| Response::new(TerminateVmResponse { vm: r.into_inner().vm }))
    }

    async fn get_vm_info(&self, request: Request<GetVmInfoRequest>) -> Result<Response<GetVmInfoResponse>, Status> {
        let req = request.into_inner();
        let vms = self.vms.read().await;
        let vm = vms.get(&req.name).ok_or_else(|| Status::not_found("vm not found"))?;
        Ok(Response::new(GetVmInfoResponse { vm: Some(Self::to_proto(vm)) }))
    }

    async fn set_default_vm(&self, request: Request<SetDefaultVmRequest>) -> Result<Response<SetDefaultVmResponse>, Status> {
        let req = request.into_inner();
        let mut vms = self.vms.write().await;
        for vm in vms.values_mut() {
            vm.default_vm = vm.name == req.name;
        }
        let vm = vms.get(&req.name).ok_or_else(|| Status::not_found("vm not found"))?;
        Ok(Response::new(SetDefaultVmResponse { vm: Some(Self::to_proto(vm)) }))
    }

    async fn snapshot_vm(&self, _: Request<SnapshotVmRequest>) -> Result<Response<SnapshotVmResponse>, Status> {
        Ok(Response::new(SnapshotVmResponse { task_id: Uuid::new_v4().to_string() }))
    }

    async fn restore_snapshot(&self, _: Request<RestoreSnapshotRequest>) -> Result<Response<RestoreSnapshotResponse>, Status> {
        Ok(Response::new(RestoreSnapshotResponse { task_id: Uuid::new_v4().to_string() }))
    }

    async fn list_snapshots(&self, _: Request<ListSnapshotsRequest>) -> Result<Response<ListSnapshotsResponse>, Status> {
        Ok(Response::new(ListSnapshotsResponse { snapshots: vec![] }))
    }

    async fn share_add(&self, request: Request<ShareAddRequest>) -> Result<Response<ShareAddResponse>, Status> {
        let req = request.into_inner();
        let mut vms = self.vms.write().await;
        let vm = vms.get_mut(&req.name).ok_or_else(|| Status::not_found("vm not found"))?;
        if let Some(share) = req.share {
            vm.shares.push(VmShare {
                host_path: share.host_path,
                guest_path: share.guest_path,
                mode: share.mode,
                backend_preference: share.backend_preference,
            });
        }
        Ok(Response::new(ShareAddResponse { vm: Some(Self::to_proto(vm)) }))
    }

    async fn share_remove(&self, request: Request<ShareRemoveRequest>) -> Result<Response<ShareRemoveResponse>, Status> {
        let req = request.into_inner();
        let mut vms = self.vms.write().await;
        let vm = vms.get_mut(&req.name).ok_or_else(|| Status::not_found("vm not found"))?;
        vm.shares.retain(|s| s.guest_path != req.guest_path);
        Ok(Response::new(ShareRemoveResponse { vm: Some(Self::to_proto(vm)) }))
    }

    async fn list_shares(&self, request: Request<ListSharesRequest>) -> Result<Response<ListSharesResponse>, Status> {
        let req = request.into_inner();
        let vms = self.vms.read().await;
        let vm = vms.get(&req.name).ok_or_else(|| Status::not_found("vm not found"))?;
        Ok(Response::new(ListSharesResponse {
            shares: vm
                .shares
                .iter()
                .map(|s| Share {
                    host_path: s.host_path.clone(),
                    guest_path: s.guest_path.clone(),
                    mode: s.mode.clone(),
                    backend_preference: s.backend_preference.clone(),
                })
                .collect(),
        }))
    }

    async fn run_command_in_vm(&self, request: Request<RunCommandInVmRequest>) -> Result<Response<Self::RunCommandInVMStream>, Status> {
        let req = request.into_inner();
        let joined = req.command.join(" ");
        let stream = tokio_stream::iter(vec![
            Ok(RunCommandChunk { kind: Some(run_command_chunk::Kind::Stdout(format!("{}\n", joined).into_bytes())) }),
            Ok(RunCommandChunk { kind: Some(run_command_chunk::Kind::ExitCode(0)) }),
        ]);
        Ok(Response::new(Box::pin(stream)))
    }

    async fn get_status(&self, _: Request<GetStatusRequest>) -> Result<Response<GetStatusResponse>, Status> {
        let vms = self.vms.read().await;
        let running = vms.values().filter(|v| v.state == VmState::Running).count() as u32;
        Ok(Response::new(GetStatusResponse {
            daemon_started_unix: self.started_unix,
            vm_total: vms.len() as u32,
            vm_running: running,
        }))
    }
}

fn internal_err(e: impl std::fmt::Display) -> Status {
    Status::internal(e.to_string())
}
