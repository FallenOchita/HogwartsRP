use serde::Deserialize;
use url::Url;

const MAX_DIRECTORY_RESPONSE_BYTES: usize = 1024 * 1024;
const DIRECTORY_PAGE_SIZE: u32 = 25;

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct DirectoryServer {
    pub server_id: String,
    pub name: String,
    pub endpoint: String,
    pub game: String,
    pub description: Option<String>,
    pub online_players: u32,
    pub max_players: u32,
    pub status: String,
    pub last_heartbeat: Option<u64>,
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct DirectoryPage {
    pub page: u32,
    pub page_size: u32,
    pub total: u64,
    pub servers: Vec<DirectoryServer>,
}

pub(crate) fn fetch_servers(
    directory_url: &str,
    search: &str,
    page: u32,
) -> Result<DirectoryPage, String> {
    let mut endpoint = Url::parse(directory_url.trim())
        .map_err(|_| "Enter a valid directory URL.".to_owned())?;
    if !matches!(endpoint.scheme(), "http" | "https")
        || endpoint.host_str().is_none()
        || !endpoint.username().is_empty()
        || endpoint.password().is_some()
        || endpoint.query().is_some()
        || endpoint.fragment().is_some()
    {
        return Err("Directory URL must be an HTTP or HTTPS origin/path without credentials, query, or fragment.".into());
    }
    endpoint.set_path("/v1/servers");
    endpoint.set_query(None);
    endpoint.set_fragment(None);
    endpoint.query_pairs_mut()
        .append_pair("page", &page.max(1).to_string())
        .append_pair("pageSize", &DIRECTORY_PAGE_SIZE.to_string())
        .append_pair("game", "hogwarts-legacy-pc");
    let search = search.trim();
    if !search.is_empty() {
        if search.len() > 80 || search.chars().any(char::is_control) {
            return Err("Search must be 1–80 characters without control characters.".into());
        }
        endpoint.query_pairs_mut().append_pair("search", search);
    }

    let client = reqwest::blocking::Client::builder()
        .timeout(std::time::Duration::from_secs(8))
        .user_agent(concat!("HogwartsRP-Launcher/", env!("CARGO_PKG_VERSION")))
        .build()
        .map_err(|error| format!("Could not create directory client: {error}"))?;
    let response = client.get(endpoint)
        .header(reqwest::header::ACCEPT, "application/json")
        .send()
        .map_err(|error| format!("Directory request failed: {error}"))?
        .error_for_status()
        .map_err(|error| format!("Directory returned an unsuccessful response: {error}"))?;
    if response.content_length().is_some_and(|length| length > MAX_DIRECTORY_RESPONSE_BYTES as u64) {
        return Err("Directory response exceeded the 1 MiB safety limit.".into());
    }
    let bytes = response.bytes()
        .map_err(|error| format!("Could not read directory response: {error}"))?;
    if bytes.len() > MAX_DIRECTORY_RESPONSE_BYTES {
        return Err("Directory response exceeded the 1 MiB safety limit.".into());
    }
    let result: DirectoryPage = serde_json::from_slice(&bytes)
        .map_err(|_| "Directory returned an invalid server-list response.".to_owned())?;
    validate_page(&result)?;
    Ok(result)
}

fn validate_page(result: &DirectoryPage) -> Result<(), String> {
    if result.page == 0 || result.page_size == 0 || result.servers.len() > result.page_size as usize {
        return Err("Directory returned invalid pagination metadata.".into());
    }
    for server in &result.servers {
        let endpoint = Url::parse(&server.endpoint)
            .map_err(|_| "Directory returned a server with an invalid endpoint.".to_owned())?;
        if server.server_id.trim().is_empty()
            || server.name.trim().is_empty()
            || server.name.len() > 80
            || !matches!(endpoint.scheme(), "http" | "https")
            || endpoint.host_str().is_none()
            || !endpoint.username().is_empty()
            || endpoint.password().is_some()
            || endpoint.query().is_some()
            || endpoint.fragment().is_some()
            || !matches!(endpoint.path(), "" | "/")
            || server.max_players == 0
            || server.online_players > server.max_players
            || !matches!(server.status.as_str(), "online" | "offline")
            || server.game != "hogwarts-legacy-pc"
        {
            return Err("Directory returned server data that failed validation.".into());
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn directory_server_response_deserializes_camel_case_fields() {
        let json = r#"{
            "page":1,"pageSize":25,"total":1,
            "servers":[{
                "serverId":"abc123","name":"Test server",
                "endpoint":"https://rp.example.net","game":"hogwarts-legacy-pc",
                "description":"A test listing","onlinePlayers":2,"maxPlayers":16,
                "status":"online","lastHeartbeat":1763500000
            }]
        }"#;
        let page: DirectoryPage = serde_json::from_str(json).unwrap();
        assert_eq!(page.servers[0].server_id, "abc123");
        assert_eq!(page.servers[0].online_players, 2);
        assert_eq!(page.servers[0].description.as_deref(), Some("A test listing"));
    }

    #[test]
    fn rejects_unsafe_or_malformed_directory_urls() {
        for url in [
            "file:///tmp/directory",
            "ftp://directory.example.net",
            "https://user:pass@example.net",
            "https://directory.example.net?token=secret",
            "not a url",
        ] {
            assert!(Url::parse(url).ok().is_some_and(|parsed| {
                matches!(parsed.scheme(), "http" | "https")
                    && parsed.host_str().is_some()
                    && parsed.username().is_empty()
                    && parsed.password().is_none()
                    && parsed.query().is_none()
                    && parsed.fragment().is_none()
            }) == false, "accepted {url}");
        }
    }

    #[test]
    fn rejects_invalid_listing_records_and_endpoints() {
        let invalid_game = DirectoryPage {
            page: 1, page_size: 25, total: 1,
            servers: vec![DirectoryServer {
                server_id: "x".into(), name: "Bad listing".into(),
                endpoint: "https://rp.example.net".into(), game: "another-game".into(),
                description: None, online_players: 0, max_players: 10,
                status: "online".into(), last_heartbeat: None,
            }],
        };
        assert!(validate_page(&invalid_game).is_err());

        let invalid_endpoint = DirectoryPage {
            page: 1, page_size: 25, total: 1,
            servers: vec![DirectoryServer {
                server_id: "x".into(), name: "Bad endpoint".into(),
                endpoint: "file:///etc/passwd".into(), game: "hogwarts-legacy-pc".into(),
                description: None, online_players: 0, max_players: 10,
                status: "online".into(), last_heartbeat: None,
            }],
        };
        assert!(validate_page(&invalid_endpoint).is_err());
    }
}
