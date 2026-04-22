use lsw_qemu::{build_qemu_command, QemuVmConfig};

#[test]
fn qemu_command_contains_required_flags() {
    let dir = tempfile::tempdir().expect("tempdir");
    let disk = dir.path().join("win11.qcow2");
    std::fs::write(&disk, b"fake").expect("write");

    let cfg = QemuVmConfig {
        disk_image: &disk,
        host_share: dir.path(),
        pid_file: &dir.path().join("vm.pid"),
        log_file: &dir.path().join("vm.log"),
        agent_socket: &dir.path().join("agent.sock"),
        ssh_port: 2222,
        memory_mb: 4096,
        cpus: 2,
    };

    let cmd = build_qemu_command(&cfg);
    let args = cmd
        .get_args()
        .map(|x| x.to_string_lossy().to_string())
        .collect::<Vec<_>>()
        .join(" ");

    assert!(args.contains("-nographic"));
    assert!(args.contains("virtio-serial-pci"));
    assert!(args.contains("virtserialport"));
    assert!(args.contains("hostfwd=tcp::2222-:22"));
}
