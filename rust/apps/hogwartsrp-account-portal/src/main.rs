use hogwartsrp_portal_auth::{portal_router, JsonPortalAccountStore, PortalHttpState, PortalOidcConfig};
use std::{env, fs, net::SocketAddr, sync::Arc};

#[tokio::main]
async fn main() -> Result<(), Box<dyn std::error::Error>> {
    let config_path = env::var("HOGWARTSRP_PORTAL_OIDC_CONFIG")
        .unwrap_or_else(|_| "config/portal-oidc.json".to_owned());
    let config_bytes = fs::read(&config_path)?;
    let config: PortalOidcConfig = serde_json::from_slice(&config_bytes)?;
    config.validate()?;
    let metadata = config.discover_provider_metadata().await?;

    let account_path = env::var("HOGWARTSRP_PORTAL_ACCOUNTS_PATH")
        .unwrap_or_else(|_| "data/portal-accounts.json".to_owned());
    let accounts = Arc::new(JsonPortalAccountStore::open(account_path)?);
    let state = PortalHttpState::new(config, metadata, accounts);

    let address: SocketAddr = env::var("HOGWARTSRP_PORTAL_LISTEN")
        .unwrap_or_else(|_| "127.0.0.1:8787".to_owned())
        .parse()?;
    let listener = tokio::net::TcpListener::bind(address).await?;
    println!("HogwartsRP central account portal listening on {address}");
    println!("Terminate TLS at a trusted reverse proxy for non-loopback deployments.");
    axum::serve(listener, portal_router(state)).await?;
    Ok(())
}
