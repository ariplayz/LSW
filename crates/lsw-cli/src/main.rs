use anyhow::{Context, Result};
use clap::{Parser, Subcommand};
use lsw_lib::auth::read_token;
use lsw_lib::paths::RuntimePaths;
use lsw_lib::proto::control_plane_client::ControlPlaneClient;
use lsw_lib::proto::{ImportImageRequest, StartRequest, StatusRequest, StopRequest};
use tokio::net::UnixStream;
use tonic::metadata::MetadataValue;
use tonic::transport::{Channel, Endpoint};
use tower::service_fn;

#[derive(Debug, Parser)]
#[command(name = "lsw")]
struct Cli {
    #[arg(short = 'd', long)]
    distro: Option<String>,

    #[arg(long, default_value = "Administrator")]
    ssh_user: String,

    #[arg(long)]
    no_shell: bool,

    #[command(subcommand)]
    command: Option<Command>,
}

#[derive(Debug, Subcommand)]
enum Command {
    Import {
        name: String,
        qcow2: String,
        #[arg(long, default_value_t = 2222)]
        ssh_port: u16,
    },
    Start {
        name: String,
    },
    Stop {
        name: String,
    },
    Status {
        name: String,
    },
}

async fn connect(socket_path: std::path::PathBuf) -> Result<ControlPlaneClient<Channel>> {
    let endpoint = Endpoint::try_from("http://[::]:50051")?;
    let channel = endpoint
        .connect_with_connector(service_fn(move |_| UnixStream::connect(socket_path.clone())))
        .await?;
    Ok(ControlPlaneClient::new(channel))
}

fn with_token<T>(token: &str, payload: T) -> Result<tonic::Request<T>> {
    let mut req = tonic::Request::new(payload);
    req.metadata_mut().insert(
        "x-lsw-token",
        MetadataValue::try_from(token).context("token contains invalid chars")?,
    );
    Ok(req)
}

#[tokio::main]
async fn main() -> Result<()> {
    let cli = Cli::parse();
    let paths = RuntimePaths::discover()?;
    let token = read_token(&paths.token_path)
        .await
        .with_context(|| format!("lswd token not found at {}", paths.token_path.display()))?;

    let mut client = connect(paths.socket_path.clone())
        .await
        .with_context(|| format!("connect to lswd at {}", paths.socket_path.display()))?;

    if let Some(distro) = cli.distro.clone() {
        let response = client
            .start(with_token(&token, StartRequest { name: distro.clone() })?)
            .await?
            .into_inner();

        println!(
            "{} {} on localhost:{} (host mount target {})",
            response.name,
            if response.started { "started" } else { "already running" },
            response.ssh_port,
            response.mount_path
        );

        if !cli.no_shell {
            let status = std::process::Command::new("ssh")
                .arg("-p")
                .arg(response.ssh_port.to_string())
                .arg(format!("{}@localhost", cli.ssh_user))
                .arg("-o")
                .arg("StrictHostKeyChecking=accept-new")
                .status()
                .context("launch ssh")?;
            std::process::exit(status.code().unwrap_or(1));
        }
        return Ok(());
    }

    match cli.command.context("use -d <name> or a subcommand")? {
        Command::Import {
            name,
            qcow2,
            ssh_port,
        } => {
            let out = client
                .import_image(with_token(
                    &token,
                    ImportImageRequest {
                        name,
                        qcow2_path: qcow2,
                        ssh_port: ssh_port as u32,
                    },
                )?)
                .await?
                .into_inner();
            println!("imported {} -> {} (ssh:{})", out.name, out.disk_image, out.ssh_port);
        }
        Command::Start { name } => {
            let out = client
                .start(with_token(&token, StartRequest { name })?)
                .await?
                .into_inner();
            println!("{} running={} ssh:{}", out.name, true, out.ssh_port);
        }
        Command::Stop { name } => {
            let out = client
                .stop(with_token(&token, StopRequest { name })?)
                .await?
                .into_inner();
            println!("{} stopped={}", out.name, out.stopped);
        }
        Command::Status { name } => {
            let out = client
                .status(with_token(&token, StatusRequest { name })?)
                .await?
                .into_inner();
            println!(
                "{} imported={} running={} disk={} ssh={}",
                out.name, out.imported, out.running, out.disk_image, out.ssh_port
            );
        }
    }

    Ok(())
}
