use hogwartsrp_directory::{router, DirectoryConfig};
use std::{env, net::SocketAddr};

#[tokio::main]
async fn main() -> Result<(), Box<dyn std::error::Error>> {
    let admin_token = env::var("HOGWARTSRP_DIRECTORY_ADMIN_TOKEN")
        .map_err(|_| "HOGWARTSRP_DIRECTORY_ADMIN_TOKEN must be configured")?;
    let database_path = env::var("HOGWARTSRP_DIRECTORY_DATABASE")
        .unwrap_or_else(|_| "data/server-directory.sqlite3".to_owned());
    let address: SocketAddr = env::var("HOGWARTSRP_DIRECTORY_LISTEN")
        .unwrap_or_else(|_| "127.0.0.1:8790".to_owned()).parse()?;
    let config = DirectoryConfig::new(database_path, &admin_token)?;
    let listener = tokio::net::TcpListener::bind(address).await?;
    println!("HogwartsRP public directory listening on {address}");
    println!("Terminate TLS at a trusted reverse proxy for non-loopback deployments.");
    axum::serve(listener, router(config)).await?;
    Ok(())
}
