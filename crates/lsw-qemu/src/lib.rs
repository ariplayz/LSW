use anyhow::Result;
use std::path::Path;
use std::process::Command;

#[derive(Debug, Clone)]
pub struct QemuVmConfig<'a> {
    pub disk_image: &'a Path,
    pub host_share: &'a Path,
    pub pid_file: &'a Path,
    pub log_file: &'a Path,
    pub agent_socket: &'a Path,
    pub ssh_port: u16,
    pub memory_mb: u32,
    pub cpus: u8,
}

pub fn build_qemu_command(cfg: &QemuVmConfig<'_>) -> Command {
    let mut cmd = Command::new("qemu-system-x86_64");
    cmd.arg("-name")
        .arg("lsw")
        .arg("-enable-kvm")
        .arg("-machine")
        .arg("q35,accel=kvm")
        .arg("-cpu")
        .arg("host")
        .arg("-smp")
        .arg(cfg.cpus.to_string())
        .arg("-m")
        .arg(cfg.memory_mb.to_string())
        .arg("-nographic")
        .arg("-display")
        .arg("none")
        .arg("-pidfile")
        .arg(cfg.pid_file)
        .arg("-drive")
        .arg(format!(
            "file={},if=virtio,format=qcow2,cache=writeback",
            cfg.disk_image.display()
        ))
        .arg("-device")
        .arg("virtio-net-pci,netdev=net0")
        .arg("-netdev")
        .arg(format!("user,id=net0,hostfwd=tcp::{}-:22", cfg.ssh_port))
        .arg("-device")
        .arg("virtio-serial-pci")
        .arg("-chardev")
        .arg(format!(
            "socket,id=lswagent,path={},server=on,wait=off",
            cfg.agent_socket.display()
        ))
        .arg("-device")
        .arg("virtserialport,chardev=lswagent,name=lsw.agent")
        .arg("-virtfs")
        .arg(format!(
            "local,path={},mount_tag=hosthome,security_model=none,readonly=off",
            cfg.host_share.display()
        ));

    let _ = cfg.log_file;
    cmd
}

pub fn is_pid_running(pid: i32) -> bool {
    nix::sys::signal::kill(nix::unistd::Pid::from_raw(pid), None).is_ok()
}

pub fn read_pid(pid_file: &Path) -> Result<i32> {
    let pid = std::fs::read_to_string(pid_file)?;
    Ok(pid.trim().parse::<i32>()?)
}
