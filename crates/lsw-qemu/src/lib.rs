use std::path::PathBuf;

use anyhow::{bail, Result};
use lsw_lib::vm::{VmRecord, VmShare};

#[derive(Debug, Clone)]
pub struct QemuLaunch {
    pub program: String,
    pub args: Vec<String>,
}

impl QemuLaunch {
    pub fn for_vm(vm: &VmRecord, runtime_dir: PathBuf) -> Result<Self> {
        if vm.name.contains(' ') {
            bail!("vm name must not contain spaces");
        }
        let mut args = vec![
            "-enable-kvm".to_string(),
            "-machine".to_string(),
            "accel=kvm".to_string(),
            "-cpu".to_string(),
            "host".to_string(),
            "-m".to_string(),
            vm.memory_mb.to_string(),
            "-smp".to_string(),
            vm.cpus.to_string(),
            "-display".to_string(),
            "none".to_string(),
            "-drive".to_string(),
            format!("if=virtio,file={},format=qcow2", vm.disk_path),
            "-netdev".to_string(),
            format!(
                "user,id=n1,hostfwd=tcp:127.0.0.1:{}-:22",
                vm.ssh_forward_port
            ),
            "-device".to_string(),
            "virtio-net-pci,netdev=n1".to_string(),
            "-chardev".to_string(),
            format!(
                "socket,id=agent,path={}",
                runtime_dir.join("agent.sock").display()
            ),
            "-device".to_string(),
            "virtserialport,chardev=agent,name=org.lsw.agent".to_string(),
        ];

        for share in &vm.shares {
            append_share(&mut args, share);
        }

        Ok(Self {
            program: "qemu-system-x86_64".to_string(),
            args,
        })
    }
}

fn append_share(args: &mut Vec<String>, share: &VmShare) {
    if share.backend_preference == "virtiofs" {
        args.push("-virtfs".to_string());
        args.push(format!(
            "local,path={},mount_tag=lswhome,security_model=none",
            share.host_path
        ));
        return;
    }

    args.push("-virtfs".to_string());
    args.push(format!(
        "local,path={},mount_tag=lswhome,security_model=mapped-file",
        share.host_path
    ));
}
