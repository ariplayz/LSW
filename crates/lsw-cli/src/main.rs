use std::process::Command;

use anyhow::{Context, Result};
use clap::{Parser, Subcommand};
use futures::StreamExt;
use lsw_lib::paths::LswPaths;
use lsw_lib::proto::lsw_control_client::LswControlClient;
use lsw_lib::proto::*;
use hyper_util::rt::TokioIo;
use tonic::transport::{Channel, Endpoint, Uri};

#[derive(Parser)]
#[command(name = "lsw")]
struct Cli {
    #[command(subcommand)]
    command: Option<Commands>,

    #[arg(short = 'd')]
    distro: Option<String>,

    #[arg(long)]
    run: Option<String>,

    #[arg(last = true)]
    cmd: Vec<String>,
}

#[derive(Subcommand)]
enum Commands {
    #[command(alias = "-l")]
    List,
    Import { name: String, qcow2: String },
    Start { name: String },
    Stop { name: String },
    Status { name: String },
}

#[tokio::main]
async fn main() -> Result<()> {
    let cli = Cli::parse();
    let mut client = connect().await?;

    if let Some(name) = cli.distro {
        attach(&mut client, &name).await?;
        return Ok(());
    }
    if let Some(name) = cli.run {
        run_cmd(&mut client, &name, cli.cmd).await?;
        return Ok(());
    }

    match cli.command.unwrap_or(Commands::List) {
        Commands::List => {
            let vms = client.list_v_ms(ListVMsRequest {}).await?.into_inner().vms;
            for vm in vms {
                println!("{}\t{}\tssh:{}", vm.name, vm.state, vm.ssh_forward_port);
            }
        }
        Commands::Import { name, qcow2 } => {
            client
                .import_vm(ImportVmRequest {
                    name,
                    qcow2_path: qcow2,
                })
                .await?;
            println!("imported");
        }
        Commands::Start { name } => {
            client.start_vm(StartVmRequest { name }).await?;
            println!("started");
        }
        Commands::Stop { name } => {
            client.stop_vm(StopVmRequest { name }).await?;
            println!("stopped");
        }
        Commands::Status { name } => {
            let vm = client
                .get_vm_info(GetVmInfoRequest { name })
                .await?
                .into_inner()
                .vm
                .unwrap();
            println!("{} state={} ssh={}", vm.name, vm.state, vm.ssh_forward_port);
        }
    }

    Ok(())
}

async fn attach(client: &mut LswControlClient<Channel>, name: &str) -> Result<()> {
    let vm = ensure_started(client, name).await?;
    let status = Command::new("ssh")
        .arg("-p")
        .arg(vm.ssh_forward_port.to_string())
        .arg(format!("{}@127.0.0.1", "lsw"))
        .arg("powershell -NoLogo -NoProfile -NoExit -Command \"Set-Location D:\\\\home\\\\user\"")
        .status()
        .context("failed to invoke ssh")?;
    std::process::exit(status.code().unwrap_or(1));
}

async fn run_cmd(client: &mut LswControlClient<Channel>, name: &str, command: Vec<String>) -> Result<()> {
    ensure_started(client, name).await?;
    let mut stream = client
        .run_command_in_vm(RunCommandInVmRequest {
            name: name.to_string(),
            command,
        })
        .await?
        .into_inner();

    let mut code = 0;
    while let Some(item) = stream.next().await {
        let chunk = item?;
        match chunk.kind {
            Some(run_command_chunk::Kind::Stdout(buf)) => print!("{}", String::from_utf8_lossy(&buf)),
            Some(run_command_chunk::Kind::Stderr(buf)) => eprint!("{}", String::from_utf8_lossy(&buf)),
            Some(run_command_chunk::Kind::ExitCode(c)) => code = c,
            None => {}
        }
    }

    std::process::exit(code);
}

async fn ensure_started(client: &mut LswControlClient<Channel>, name: &str) -> Result<VmInfo> {
    client.start_vm(StartVmRequest { name: name.to_string() }).await?;
    let vm = client
        .get_vm_info(GetVmInfoRequest {
            name: name.to_string(),
        })
        .await?
        .into_inner()
        .vm
        .ok_or_else(|| anyhow::anyhow!("missing vm info"))?;
    Ok(vm)
}

async fn connect() -> Result<LswControlClient<Channel>> {
    let paths = LswPaths::discover()?;
    let sock = paths.control_socket();
    let endpoint = Endpoint::try_from("http://[::]:50051")?;
    let channel = endpoint
        .connect_with_connector(tower::service_fn(move |_: Uri| {
            let sock = sock.clone();
            async move {
                let stream = tokio::net::UnixStream::connect(sock).await?;
                Ok::<_, std::io::Error>(TokioIo::new(stream))
            }
        }))
        .await
        .map_err(map_status)?;
    Ok(LswControlClient::new(channel))
}

fn map_status(err: tonic::transport::Error) -> anyhow::Error {
    anyhow::anyhow!("transport error: {err}")
}
