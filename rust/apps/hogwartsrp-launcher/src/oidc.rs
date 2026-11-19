//! Browser-based OIDC authorization-code flow with PKCE for server join tickets.
//! The selected server validates the ID token; this client never treats a token as a local permission grant.

use base64::{engine::general_purpose::URL_SAFE_NO_PAD, Engine as _};
use rand::{rngs::OsRng, RngCore};
use reqwest::blocking::Client;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::{
    io::{BufRead, BufReader, Write},
    net::{IpAddr, Ipv4Addr, SocketAddr, TcpListener, TcpStream},
    time::{Duration, Instant},
};
use url::{form_urlencoded, Url};

use crate::launcher_core::validate_server_endpoint;

#[derive(Clone)]
pub struct OidcJoinCredentialProvider {
    issuer: Url,
    client_id: String,
    http: Client,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct ServerChallenge {
    challenge_id: String,
    nonce: String,
}

#[derive(Deserialize)]
struct ProviderMetadata {
    issuer: String,
    authorization_endpoint: String,
    token_endpoint: String,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct TokenResponse {
    access_token: String,
    token_type: String,
    id_token: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct TicketRequest<'a> {
    challenge_id: &'a str,
    id_token: &'a str,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct TicketResponse {
    credential: String,
    expires_at: String,
}

impl OidcJoinCredentialProvider {
    pub fn new(issuer: &str, client_id: &str) -> Result<Self, String> {
        let issuer = Url::parse(issuer.trim()).map_err(|_| "OIDC issuer must be an absolute HTTPS URL.".to_owned())?;
        if !is_secure_url(&issuer) || issuer.query().is_some() || issuer.fragment().is_some() {
            return Err("OIDC issuer must use HTTPS (HTTP is allowed only on loopback for local development).".into());
        }
        let client_id = client_id.trim();
        if client_id.is_empty() || client_id.len() > 512 || client_id.chars().any(char::is_control) {
            return Err("A valid OIDC client ID is required.".into());
        }
        let http = Client::builder()
            .timeout(Duration::from_secs(30))
            .redirect(reqwest::redirect::Policy::none())
            .build().map_err(|error| error.to_string())?;
        Ok(Self { issuer, client_id: client_id.to_owned(), http })
    }

    pub fn get_join_credential(&self, server_endpoint: &str) -> Result<String, String> {
        let endpoint = validate_server_endpoint(server_endpoint)?;
        let server_base = server_http_base(&endpoint)?;
        let challenge: ServerChallenge = self.http.get(server_base.join("auth/oidc/challenge").map_err(|e| e.to_string())?)
            .send().map_err(|e| e.to_string())?
            .error_for_status().map_err(|_| "Selected server is not configured for OIDC sign-in or could not create a challenge.".to_owned())?
            .json().map_err(|e| e.to_string())?;
        if challenge.challenge_id.trim().is_empty() || challenge.nonce.trim().is_empty() {
            return Err("Server returned an invalid OIDC sign-in challenge.".into());
        }

        let metadata = self.discover_provider()?;
        let state = random_token(32);
        let verifier = random_token(48);
        let challenge_hash = URL_SAFE_NO_PAD.encode(Sha256::digest(verifier.as_bytes()));
        let listener = TcpListener::bind(SocketAddr::new(IpAddr::V4(Ipv4Addr::LOCALHOST), 0))
            .map_err(|e| format!("Could not start the local OIDC callback listener: {e}"))?;
        listener.set_nonblocking(true).map_err(|e| e.to_string())?;
        let port = listener.local_addr().map_err(|e| e.to_string())?.port();
        let redirect_uri = Url::parse(&format!("http://127.0.0.1:{port}/callback/")).map_err(|e| e.to_string())?;
        let mut authorization = metadata.authorization_endpoint.clone();
        authorization.query_pairs_mut()
            .append_pair("client_id", &self.client_id)
            .append_pair("redirect_uri", redirect_uri.as_str())
            .append_pair("response_type", "code")
            .append_pair("scope", "openid profile")
            .append_pair("state", &state)
            .append_pair("nonce", &challenge.nonce)
            .append_pair("code_challenge", &challenge_hash)
            .append_pair("code_challenge_method", "S256");

        open::that(authorization.as_str())
            .map_err(|e| format!("Unable to open the system browser for sign-in: {e}"))?;
        let code = receive_authorization_code(&listener, &redirect_uri, &state, Duration::from_secs(300))?;

        let token_response: TokenResponse = self.http.post(metadata.token_endpoint)
            .form(&[
                ("grant_type", "authorization_code"),
                ("code", code.as_str()),
                ("redirect_uri", redirect_uri.as_str()),
                ("client_id", self.client_id.as_str()),
                ("code_verifier", verifier.as_str()),
            ])
            .send().map_err(|e| e.to_string())?
            .error_for_status().map_err(|_| "Identity provider rejected the authorization-code exchange.".to_owned())?
            .json().map_err(|e| e.to_string())?;
        if !token_response.token_type.eq_ignore_ascii_case("bearer")
            || token_response.access_token.trim().is_empty() || token_response.id_token.trim().is_empty() {
            return Err("Identity provider returned an incomplete OIDC token response.".into());
        }

        let ticket: TicketResponse = self.http.post(server_base.join("auth/tickets").map_err(|e| e.to_string())?)
            .json(&TicketRequest { challenge_id: &challenge.challenge_id, id_token: &token_response.id_token })
            .send().map_err(|e| e.to_string())?
            .error_for_status().map_err(|_| "Selected server rejected the OIDC sign-in or could not issue a join ticket.".to_owned())?
            .json().map_err(|e| e.to_string())?;
        if ticket.credential.trim().is_empty() || ticket.expires_at.trim().is_empty() {
            return Err("Server returned an invalid one-time join ticket.".into());
        }
        Ok(ticket.credential)
    }

    fn discover_provider(&self) -> Result<ProviderEndpoints, String> {
        let discovery_url = self.issuer.as_str().trim_end_matches('/').to_owned() + "/.well-known/openid-configuration";
        let discovery = Url::parse(&discovery_url).map_err(|e| e.to_string())?;
        let metadata: ProviderMetadata = self.http.get(discovery).send().map_err(|e| e.to_string())?
            .error_for_status().map_err(|_| "OIDC provider discovery failed.".to_owned())?
            .json().map_err(|e| e.to_string())?;
        if metadata.issuer != self.issuer.as_str() {
            return Err("OIDC provider metadata issuer does not match the configured issuer.".into());
        }
        let authorization_endpoint = Url::parse(&metadata.authorization_endpoint).map_err(|e| e.to_string())?;
        let token_endpoint = Url::parse(&metadata.token_endpoint).map_err(|e| e.to_string())?;
        if !is_secure_url(&authorization_endpoint) || !is_secure_url(&token_endpoint) {
            return Err("OIDC provider endpoints must use HTTPS (HTTP is allowed only on loopback).".into());
        }
        Ok(ProviderEndpoints { authorization_endpoint, token_endpoint })
    }
}

struct ProviderEndpoints {
    authorization_endpoint: Url,
    token_endpoint: Url,
}

fn server_http_base(endpoint: &Url) -> Result<Url, String> {
    let mut base = endpoint.clone();
    let scheme = match endpoint.scheme() {
        "https" | "wss" => "https",
        "http" | "ws" if is_loopback(endpoint) => "http",
        _ => return Err("Remote authentication requires HTTPS/WSS.".into()),
    };
    base.set_scheme(scheme).map_err(|_| "Invalid server URL scheme.".to_owned())?;
    base.set_path("/");
    base.set_query(None);
    base.set_fragment(None);
    Ok(base)
}

fn is_secure_url(url: &Url) -> bool {
    url.scheme() == "https" || (url.scheme() == "http" && is_loopback(url))
}

fn is_loopback(url: &Url) -> bool {
    url.host_str().is_some_and(|host| host.eq_ignore_ascii_case("localhost")
        || host == "127.0.0.1" || host == "::1")
}

fn random_token(byte_count: usize) -> String {
    let mut bytes = vec![0_u8; byte_count];
    OsRng.fill_bytes(&mut bytes);
    URL_SAFE_NO_PAD.encode(bytes)
}

fn receive_authorization_code(
    listener: &TcpListener,
    redirect_uri: &Url,
    expected_state: &str,
    timeout: Duration,
) -> Result<String, String> {
    let deadline = Instant::now() + timeout;
    loop {
        if Instant::now() >= deadline {
            return Err("Timed out waiting for browser sign-in.".into());
        }
        match listener.accept() {
            Ok((mut stream, peer)) => {
                if !peer.ip().is_loopback() {
                    write_response(&mut stream, 403, "This sign-in callback is local to the launcher.");
                    continue;
                }
                match parse_callback(&mut stream, redirect_uri, expected_state) {
                    Ok(code) => {
                        write_response(&mut stream, 200, "Sign-in received. You may return to HogwartsRP and close this page.");
                        return Ok(code);
                    },
                    Err(CallbackError::Retry(status, message)) => {
                        write_response(&mut stream, status, message);
                    }
                    Err(CallbackError::Fatal(status, message)) => {
                        write_response(&mut stream, status, message);
                        return Err(message.to_owned());
                    }
                }
            }
            Err(error) if error.kind() == std::io::ErrorKind::WouldBlock => {
                std::thread::sleep(Duration::from_millis(50));
            }
            Err(error) => return Err(format!("OIDC callback listener failed: {error}")),
        }
    }
}

enum CallbackError {
    Retry(u16, &'static str),
    Fatal(u16, &'static str),
}

fn parse_callback(stream: &mut TcpStream, redirect_uri: &Url, expected_state: &str) -> Result<String, CallbackError> {
    let mut reader = BufReader::new(stream.try_clone().map_err(|_| CallbackError::Retry(400, "Invalid callback."))?);
    let mut request_line = String::new();
    reader.read_line(&mut request_line).map_err(|_| CallbackError::Retry(400, "Invalid callback."))?;
    if request_line.len() > 4096 { return Err(CallbackError::Retry(400, "Callback request was too large.")); }
    let parts: Vec<_> = request_line.split_whitespace().collect();
    if parts.len() != 3 || parts[0] != "GET" {
        return Err(CallbackError::Retry(400, "Invalid sign-in callback."));
    }
    let mut host = None;
    let mut header_bytes = 0;
    loop {
        let mut line = String::new();
        reader.read_line(&mut line).map_err(|_| CallbackError::Retry(400, "Invalid callback headers."))?;
        header_bytes += line.len();
        if header_bytes > 8192 { return Err(CallbackError::Retry(400, "Callback headers were too large.")); }
        if line == "\r\n" || line == "\n" { break; }
        if let Some((key, value)) = line.split_once(':') {
            if key.eq_ignore_ascii_case("host") {
                if host.is_some() { return Err(CallbackError::Retry(400, "Duplicate Host header.")); }
                host = Some(value.trim().to_owned());
            }
        }
    }
    let expected_host = format!("{}:{}", redirect_uri.host_str().unwrap_or_default(), redirect_uri.port().unwrap_or_default());
    if host.as_deref() != Some(expected_host.as_str()) {
        return Err(CallbackError::Retry(400, "Invalid callback host."));
    }
    let callback = redirect_uri.join(parts[1]).map_err(|_| CallbackError::Retry(400, "Invalid callback URL."))?;
    if callback.origin() != redirect_uri.origin() {
        return Err(CallbackError::Retry(400, "Invalid callback origin."));
    }
    if callback.path() != "/callback/" {
        return Err(CallbackError::Retry(404, "Callback not found."));
    }
    let mut query = std::collections::HashMap::new();
    for (key, value) in form_urlencoded::parse(callback.query().unwrap_or_default().as_bytes()) {
        if query.insert(key.into_owned(), value.into_owned()).is_some() {
            return Err(CallbackError::Retry(400, "Duplicate sign-in callback parameter."));
        }
    }
    if !query.get("state").is_some_and(|state| fixed_time_eq(state, expected_state)) {
        return Err(CallbackError::Retry(400, "Sign-in response did not match. Return to the launcher."));
    }
    if query.contains_key("error") {
        return Err(CallbackError::Fatal(400, "Identity provider did not complete sign-in."));
    }
    query.get("code").filter(|code| !code.trim().is_empty()).cloned()
        .ok_or(CallbackError::Fatal(400, "Identity provider returned no authorization code."))
}

fn fixed_time_eq(left: &str, right: &str) -> bool {
    let left = left.as_bytes();
    let right = right.as_bytes();
    if left.len() != right.len() { return false; }
    left.iter().zip(right).fold(0_u8, |difference, (a, b)| difference | (a ^ b)) == 0
}

fn write_response(stream: &mut TcpStream, status: u16, message: &str) {
    let status_text = match status { 200 => "OK", 403 => "Forbidden", 404 => "Not Found", _ => "Bad Request" };
    let body = format!("<!doctype html><meta charset=\"utf-8\"><title>HogwartsRP sign-in</title><p>{}</p>",
        html_escape(message));
    let response = format!("HTTP/1.1 {status} {status_text}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {}\r\nCache-Control: no-store\r\nPragma: no-cache\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nConnection: close\r\n\r\n{body}",
        body.len());
    let _ = stream.write_all(response.as_bytes());
    let _ = stream.flush();
}

fn html_escape(value: &str) -> String {
    value.replace('&', "&amp;").replace('<', "&lt;").replace('>', "&gt;")
        .replace('"', "&quot;").replace('\'', "&#39;")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rejects_insecure_remote_issuer() {
        assert!(OidcJoinCredentialProvider::new("http://identity.example", "launcher").is_err());
        assert!(OidcJoinCredentialProvider::new("http://127.0.0.1:8080", "launcher").is_ok());
    }

    #[test]
    fn loopback_server_url_is_normalized_for_http_auth_endpoints() {
        let endpoint = Url::parse("ws://127.0.0.1:7777/connect").unwrap();
        assert_eq!(server_http_base(&endpoint).unwrap().as_str(), "http://127.0.0.1:7777/");
    }

    #[test]
    fn remote_http_server_is_rejected() {
        let endpoint = Url::parse("http://example.test").unwrap();
        assert!(server_http_base(&endpoint).is_err());
    }
}
