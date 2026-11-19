use axum::{
    extract::{Path, Query, State},
    http::{header, HeaderMap, StatusCode},
    response::{IntoResponse, Response},
    routing::{get, put},
    Json, Router,
};
use base64::{engine::general_purpose::URL_SAFE_NO_PAD, Engine as _};
use rand::RngCore;
use rusqlite::{params, Connection, OptionalExtension};
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::{net::IpAddr, path::PathBuf, sync::Arc, time::{SystemTime, UNIX_EPOCH}};
use subtle::ConstantTimeEq;
use url::Url;

const HEARTBEAT_TTL_SECONDS: u64 = 90;
const MAX_PAGE_SIZE: u32 = 100;

#[derive(Clone)]
pub struct DirectoryConfig {
    database_path: PathBuf,
    admin_token_hash: [u8; 32],
}

impl DirectoryConfig {
    pub fn new(database_path: impl Into<PathBuf>, admin_token: &str) -> Result<Self, String> {
        if admin_token.len() < 32 || admin_token.len() > 512 {
            return Err("Directory admin token must contain 32 to 512 characters.".into());
        }
        Ok(Self { database_path: database_path.into(), admin_token_hash: digest(admin_token.as_bytes()) })
    }
}

#[derive(Clone)]
struct AppState(Arc<DirectoryConfig>);

pub fn router(config: DirectoryConfig) -> Router {
    Router::new()
        .route("/health", get(health))
        .route("/v1/servers", get(list_servers).post(register_server))
        .route("/v1/servers/{server_id}/heartbeat", put(heartbeat))
        .with_state(AppState(Arc::new(config)))
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct RegisterRequest {
    name: String,
    endpoint: String,
    game: String,
    max_players: u32,
    description: Option<String>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct RegisterResponse {
    server_id: String,
    server_credential: String,
}

#[derive(Deserialize)]
struct ListQuery {
    page: Option<u32>,
    page_size: Option<u32>,
    game: Option<String>,
    search: Option<String>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ServerListing {
    server_id: String,
    name: String,
    endpoint: String,
    game: String,
    description: Option<String>,
    online_players: u32,
    max_players: u32,
    status: &'static str,
    last_heartbeat: Option<u64>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ServerPage {
    page: u32,
    page_size: u32,
    total: u64,
    servers: Vec<ServerListing>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct HeartbeatRequest {
    online_players: u32,
}

async fn health() -> &'static str { "ok" }

async fn register_server(State(state): State<AppState>, headers: HeaderMap, Json(input): Json<RegisterRequest>) -> Response {
    if !authorized_admin(&headers, &state.0.admin_token_hash) {
        return error(StatusCode::UNAUTHORIZED, "Directory administration authorization is required.");
    }
    let name = input.name.trim();
    let game = input.game.trim().to_ascii_lowercase();
    let description = input.description.as_deref().map(str::trim).filter(|s| !s.is_empty());
    if name.is_empty() || name.len() > 80 || name.chars().any(char::is_control)
        || !matches!(game.as_str(), "hogwarts-legacy-pc")
        || !(1..=5000).contains(&input.max_players)
        || description.is_some_and(|s| s.len() > 500 || s.chars().any(char::is_control))
        || !valid_public_endpoint(&input.endpoint)
    {
        return error(StatusCode::BAD_REQUEST, "The server listing contains invalid fields.");
    }

    let server_id = random_token(18);
    let credential = random_token(32);
    let credential_hash = digest(credential.as_bytes());
    let db_path = state.0.database_path.clone();
    let row = (server_id.clone(), name.to_owned(), input.endpoint.clone(), game, description.map(str::to_owned), input.max_players, credential_hash.to_vec());
    let result = tokio::task::spawn_blocking(move || {
        let conn = open_db(&db_path)?;
        conn.execute(
            "INSERT INTO servers (server_id,name,endpoint,game,description,max_players,credential_hash,created_at) VALUES (?1,?2,?3,?4,?5,?6,?7,?8)",
            params![row.0,row.1,row.2,row.3,row.4,row.5,row.6,now()],
        ).map_err(|_| ())?;
        Ok::<(), ()>(())
    }).await;
    match result {
        Ok(Ok(())) => Json(RegisterResponse { server_id, server_credential: credential }).into_response(),
        _ => error(StatusCode::INTERNAL_SERVER_ERROR, "The server listing could not be registered."),
    }
}

async fn heartbeat(State(state): State<AppState>, Path(server_id): Path<String>, headers: HeaderMap, Json(input): Json<HeartbeatRequest>) -> Response {
    let Some(credential) = bearer(&headers) else { return error(StatusCode::UNAUTHORIZED, "A server credential is required."); };
    let db_path = state.0.database_path.clone();
    let hash = digest(credential.as_bytes());
    let result = tokio::task::spawn_blocking(move || {
        let conn = open_db(&db_path)?;
        let stored: Option<(Vec<u8>, u32)> = conn.query_row(
            "SELECT credential_hash,max_players FROM servers WHERE server_id=?1 AND revoked_at IS NULL",
            [&server_id], |row| Ok((row.get(0)?, row.get(1)?))
        ).optional().map_err(|_| ())?;
        let Some((stored_hash, max_players)) = stored else { return Ok::<bool, ()>(false); };
        if stored_hash.len() != hash.len() || stored_hash.as_slice().ct_eq(hash.as_slice()).unwrap_u8() != 1 || input.online_players > max_players {
            return Ok(false);
        }
        conn.execute("UPDATE servers SET online_players=?1,last_heartbeat=?2 WHERE server_id=?3", params![input.online_players,now(),server_id]).map_err(|_| ())?;
        Ok(true)
    }).await;
    match result {
        Ok(Ok(true)) => StatusCode::NO_CONTENT.into_response(),
        Ok(Ok(false)) => error(StatusCode::UNAUTHORIZED, "The server credential or heartbeat is invalid."),
        _ => error(StatusCode::INTERNAL_SERVER_ERROR, "The heartbeat could not be stored."),
    }
}

async fn list_servers(State(state): State<AppState>, Query(query): Query<ListQuery>) -> Response {
    let page = query.page.unwrap_or(1).clamp(1, 1_000_000);
    let page_size = query.page_size.unwrap_or(25).clamp(1, MAX_PAGE_SIZE);
    let game = query.game.as_deref().map(str::trim).filter(|s| !s.is_empty()).map(str::to_ascii_lowercase);
    if game.as_deref().is_some_and(|g| g != "hogwarts-legacy-pc") {
        return error(StatusCode::BAD_REQUEST, "The requested game filter is not supported.");
    }
    let search = query.search.as_deref().map(str::trim).filter(|s| !s.is_empty()).map(str::to_owned);
    if search.as_deref().is_some_and(|s| s.len() > 80 || s.chars().any(char::is_control)) {
        return error(StatusCode::BAD_REQUEST, "The search query is invalid.");
    }
    let db_path = state.0.database_path.clone();
    let offset = (page - 1) * page_size;
    let result = tokio::task::spawn_blocking(move || {
        let conn = open_db(&db_path)?;
        let pattern = search.map(|s| format!("%{}%", s.replace('%', "\\%").replace('_', "\\_")));
        let total: u64 = conn.query_row(
            "SELECT COUNT(*) FROM servers WHERE revoked_at IS NULL AND (?1 IS NULL OR game=?1) AND (?2 IS NULL OR name LIKE ?2 ESCAPE '\\')",
            params![game, pattern], |row| row.get(0)
        ).map_err(|_| ())?;
        let mut stmt = conn.prepare(
            "SELECT server_id,name,endpoint,game,description,online_players,max_players,last_heartbeat FROM servers WHERE revoked_at IS NULL AND (?1 IS NULL OR game=?1) AND (?2 IS NULL OR name LIKE ?2 ESCAPE '\\') ORDER BY name COLLATE NOCASE,server_id LIMIT ?3 OFFSET ?4"
        ).map_err(|_| ())?;
        let rows = stmt.query_map(params![game, pattern, page_size, offset], |row| {
            let heartbeat: Option<u64> = row.get(7)?;
            let online: u32 = row.get(5)?;
            let max: u32 = row.get(6)?;
            let is_online = heartbeat.is_some_and(|stamp| now().saturating_sub(stamp) <= HEARTBEAT_TTL_SECONDS);
            Ok(ServerListing {
                server_id: row.get(0)?, name: row.get(1)?, endpoint: row.get(2)?, game: row.get(3)?,
                description: row.get(4)?, online_players: if is_online { online } else { 0 }, max_players: max,
                status: if is_online { "online" } else { "offline" }, last_heartbeat: heartbeat,
            })
        }).map_err(|_| ())?;
        let mut servers = Vec::new();
        for row in rows { servers.push(row.map_err(|_| ())?); }
        Ok::<ServerPage, ()>(ServerPage { page, page_size, total, servers })
    }).await;
    match result {
        Ok(Ok(page)) => Json(page).into_response(),
        _ => error(StatusCode::INTERNAL_SERVER_ERROR, "The public directory could not be read."),
    }
}

fn open_db(path: &PathBuf) -> Result<Connection, ()> {
    if let Some(parent) = path.parent() { std::fs::create_dir_all(parent).map_err(|_| ())?; }
    let conn = Connection::open(path).map_err(|_| ())?;
    conn.execute_batch(
        "PRAGMA journal_mode=WAL;
         PRAGMA foreign_keys=ON;
         CREATE TABLE IF NOT EXISTS servers (
           server_id TEXT PRIMARY KEY,
           name TEXT NOT NULL,
           endpoint TEXT NOT NULL,
           game TEXT NOT NULL,
           description TEXT,
           online_players INTEGER NOT NULL DEFAULT 0,
           max_players INTEGER NOT NULL,
           credential_hash BLOB NOT NULL,
           created_at INTEGER NOT NULL,
           last_heartbeat INTEGER,
           revoked_at INTEGER
         );
         CREATE INDEX IF NOT EXISTS idx_servers_public_order ON servers(revoked_at,game,name);"
    ).map_err(|_| ())?;
    Ok(conn)
}

fn authorized_admin(headers: &HeaderMap, expected: &[u8; 32]) -> bool {
    bearer(headers).is_some_and(|token| digest(token.as_bytes()).as_slice().ct_eq(expected).unwrap_u8() == 1)
}

fn bearer(headers: &HeaderMap) -> Option<String> {
    let value = headers.get(header::AUTHORIZATION)?.to_str().ok()?;
    let (scheme, token) = value.split_once(' ')?;
    if !scheme.eq_ignore_ascii_case("bearer") || token.is_empty() || token.len() > 512 || token.chars().any(char::is_control) { return None; }
    Some(token.to_owned())
}

fn valid_public_endpoint(value: &str) -> bool {
    if value.len() > 253 { return false; }
    let Ok(url) = Url::parse(value) else { return false; };
    if !matches!(url.scheme(), "https" | "http") || url.host_str().is_none()
        || !url.username().is_empty() || url.password().is_some()
        || url.query().is_some() || url.fragment().is_some()
        || !matches!(url.path(), "" | "/")
    { return false; }
    let host = url.host_str().unwrap_or_default().trim_end_matches('.').to_ascii_lowercase();
    if host == "localhost" || host.ends_with(".localhost") || host.ends_with(".local")
        || host.ends_with(".internal") || host.ends_with(".test") || host.ends_with(".invalid")
        || host.ends_with(".example")
    { return false; }
    if let Ok(ip) = host.parse::<IpAddr>() {
        return is_public_ip(ip) && (url.scheme() == "https" || !matches!(ip, IpAddr::V4(v4) if v4.is_loopback()));
    }
    host.contains('.') && host.split('.').all(|label| !label.is_empty() && label.len() <= 63 && label.bytes().all(|b| b.is_ascii_alphanumeric() || b == b'-'))
}

fn is_public_ip(ip: IpAddr) -> bool {
    match ip {
        IpAddr::V4(v4) => {
            let [a,b,_,_] = v4.octets();
            !(a == 0 || a == 10 || a == 127 || a >= 224 || (a == 100 && (64..=127).contains(&b))
                || (a == 169 && b == 254) || (a == 172 && (16..=31).contains(&b))
                || (a == 192 && (b == 168 || b == 0)) || (a == 198 && (b == 18 || b == 19)))
        }
        IpAddr::V6(v6) => {
            let octets = v6.octets();
            !(v6.is_loopback() || v6.is_unspecified() || (octets[0] & 0xfe) == 0xfc
                || (octets[0] == 0xfe && (octets[1] & 0xc0) == 0x80) || octets[0] == 0xff
                || v6.to_ipv4_mapped().is_some_and(|mapped| !is_public_ip(IpAddr::V4(mapped))))
        }
    }
}

fn digest(value: &[u8]) -> [u8; 32] { Sha256::digest(value).into() }

fn random_token(bytes: usize) -> String {
    let mut value = vec![0u8; bytes];
    rand::rngs::OsRng.fill_bytes(&mut value);
    URL_SAFE_NO_PAD.encode(value)
}

fn now() -> u64 { SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default().as_secs() }

fn error(status: StatusCode, message: &'static str) -> Response { (status, message).into_response() }

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rejects_private_and_local_endpoints() {
        for endpoint in ["http://127.0.0.1:9000", "http://192.168.1.4", "http://10.0.0.2", "http://localhost:8787", "https://printer.local"] {
            assert!(!valid_public_endpoint(endpoint), "accepted {endpoint}");
        }
    }

    #[test]
    fn accepts_public_https_host_and_rejects_url_components() {
        assert!(valid_public_endpoint("https://rp.example.net:443"));
        assert!(!valid_public_endpoint("https://user:pass@example.net"));
        assert!(!valid_public_endpoint("https://example.net/path"));
        assert!(!valid_public_endpoint("https://example.net/?token=x"));
    }

    #[test]
    fn rejects_non_public_ip_ranges() {
        for ip in ["0.0.0.1", "100.64.0.1", "169.254.1.1", "172.20.1.1", "192.168.1.1", "224.0.0.1", "::1", "fc00::1", "fe80::1"] {
            assert!(!is_public_ip(ip.parse().unwrap()), "accepted {ip}");
        }
        assert!(is_public_ip("8.8.8.8".parse().unwrap()));
    }

    #[test]
    fn bearer_parser_requires_one_bounded_token() {
        let mut headers = HeaderMap::new();
        headers.insert(header::AUTHORIZATION, "Bearer abc123".parse().unwrap());
        assert_eq!(bearer(&headers).as_deref(), Some("abc123"));
        headers.insert(header::AUTHORIZATION, "Basic abc123".parse().unwrap());
        assert!(bearer(&headers).is_none());
    }
}
