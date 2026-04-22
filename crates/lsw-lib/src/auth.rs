use anyhow::{Context, Result};
use rand::RngCore;
use std::os::unix::fs::PermissionsExt;
use std::path::Path;

pub async fn ensure_token_file(path: &Path) -> Result<String> {
    if tokio::fs::try_exists(path).await? {
        return read_token(path).await;
    }

    let mut raw = [0_u8; 32];
    rand::rngs::OsRng.fill_bytes(&mut raw);
    let token = hex::encode(raw);

    tokio::fs::write(path, format!("{token}\n"))
        .await
        .with_context(|| format!("write token {}", path.display()))?;

    let perms = std::fs::Permissions::from_mode(0o600);
    tokio::fs::set_permissions(path, perms)
        .await
        .with_context(|| format!("chmod token {}", path.display()))?;

    Ok(token)
}

pub async fn read_token(path: &Path) -> Result<String> {
    let token = tokio::fs::read_to_string(path)
        .await
        .with_context(|| format!("read token {}", path.display()))?;
    Ok(token.trim().to_owned())
}
