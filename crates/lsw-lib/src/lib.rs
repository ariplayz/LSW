pub mod auth;
pub mod model;
pub mod paths;

pub mod proto {
    tonic::include_proto!("lsw.control");
}
