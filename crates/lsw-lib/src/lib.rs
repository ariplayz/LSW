pub mod config;
pub mod paths;
pub mod vm;

pub mod proto {
    tonic::include_proto!("lsw.control.v1");
}

pub const CONTROL_SOCK: &str = "control.sock";
