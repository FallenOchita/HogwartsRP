use super::PortalHttpState;
use crate::AccountLinkProvider;
use axum::{
    extract::{RawQuery, State},
    http::{header, HeaderMap, HeaderValue, StatusCode},
    response::{IntoResponse, Redirect, Response},
    routing::{get, post},
    Json, Router,
};
use serde::{Deserialize, Serialize};
use std::{collections::BTreeMap, env, time::Duration};
use url::{form_urlencoded, Url};

const EPIC_AUTHORIZE_ENDPOINT: &str = "https://www.epicgames.com/id/authorize";
const EPIC_TOKEN_ENDPOINT: &str = "https://api.epicgames.dev/epic/oauth/v2/token";
const EPIC_TOKEN_INFO_ENDPOINT: &str = "https://api.epicgames.dev/epic/oauth/v2/tokenInfo";
const PROVIDER_HTTP_TIMEOUT: Duration = Duration::from_secs(8);
const MAX_PROVIDER_RESPONSE_BYTES: usize = 32 * 1024;

#[derive(Clone)]
pub struct EpicLinkConfig {
    client_id: String,
    client_secret: String,
    deployment_id: String,
    redirect_uri: String,
}

impl EpicLinkConfig {
    /// Read optional server-only Epic EOS OAuth settings. Partial configuration
    /// is rejected instead of silently disabling a misconfigured provider.
    pub fn from_env() -> Result<Option<Self>, String> {
        let client_id = env::var("HOGWARTSRP_EPIC_CLIENT_ID").ok();
        let client_secret = env::var("HOGWARTSRP_EPIC_CLIENT_SECRET").ok();
        let deployment_id = env::var("HOGWARTSRP_EPIC_DEPLOYMENT_ID").ok();
        let redirect_uri = env::var("HOGWARTSRP_EPIC_REDIRECT_URI").ok();
        if client_id.is_none() && client_secret.is_none()
            && deployment_id.is_none() && redirect_uri.is_none()
        {
            return Ok(None);
        }
        let (Some(client_id), Some(client_secret), Some(deployment_id), Some(redirect_uri)) =
            (client_id, client_secret, deployment_id, redirect_uri)
        else {
            return Err("Epic account linking requires client ID, client secret, deployment ID, and redirect URI.".into());
        };
        if client_id.trim().is_empty() || client_id.len() > 256
            || client_secret.trim().is_empty() || client_secret.len() > 1024
            || deployment_id.trim().is_empty() || deployment_id.len() > 256
            || client_id.chars().any(char::is_control)
            || client_secret.chars().any(char::is_control)
            || deployment_id.chars().any(char::is_control)
            || !valid_redirect_uri(&redirect_uri)
            || Url::parse(&redirect_uri).ok().is_none_or(|url| url.path() != "/account/links/epic/callback")
        {
            return Err("Epic account-link configuration is invalid.".into());
        }
        Ok(Some(Self { client_id, client_secret, deployment_id, redirect_uri }))
    }

    fn authorization_url(&self, state: &str) -> Result<String, ()> {
        let mut url = Url::parse(EPIC_AUTHORIZE_ENDPOINT).map_err(|_| ())?;
        url.query_pairs_mut()
            .append_pair("client_id", &self.client_id)
            .append_pair("response_type", "code")
            .append_pair("scope", "basic_profile")
            .append_pair("redirect_uri", &self.redirect_uri)
            .append_pair("state", state);
        Ok(url.to_string())
    }

    async fn exchange_and_verify(&self, code: &str) -> Result<String, ()> {
        if code.trim().is_empty() || code.len() > 2048 || code.chars().any(char::is_control) {
            return Err(());
        }
        let client = reqwest::Client::builder()
            .redirect(reqwest::redirect::Policy::none())
            .timeout(PROVIDER_HTTP_TIMEOUT)
            .build()
            .map_err(|_| ())?;

        let token_response = client.post(EPIC_TOKEN_ENDPOINT)
            .basic_auth(&self.client_id, Some(&self.client_secret))
            .form(&[
                ("grant_type", "authorization_code"),
                ("deployment_id", self.deployment_id.as_str()),
                ("scope", "basic_profile"),
                ("code", code),
                ("redirect_uri", self.redirect_uri.as_str()),
            ])
            .send().await.map_err(|_| ())?;
        if !token_response.status().is_success() {
            return Err(());
        }
        let token_body = read_limited(token_response).await?;
        let token: EpicTokenResponse = serde_json::from_slice(&token_body).map_err(|_| ())?;
        if !token.token_type.eq_ignore_ascii_case("bearer")
            || token.access_token.is_empty()
            || token.access_token.len() > 8192
            || token.access_token.chars().any(char::is_control)
        {
            return Err(());
        }

        let info_response = client.post(EPIC_TOKEN_INFO_ENDPOINT)
            .form(&[("token", token.access_token.as_str())])
            .send().await.map_err(|_| ())?;
        if !info_response.status().is_success() {
            return Err(());
        }
        let info_body = read_limited(info_response).await?;
        let info: EpicTokenInfo = serde_json::from_slice(&info_body).map_err(|_| ())?;
        verified_epic_account_id(&info, &self.client_id)
    }
}

#[derive(Deserialize)]
struct EpicTokenResponse {
    access_token: String,
    token_type: String,
}

#[derive(Deserialize)]
struct EpicTokenInfo {
    active: bool,
    token_type: String,
    client_id: String,
    account_id: String,
}

fn verified_epic_account_id(info: &EpicTokenInfo, expected_client_id: &str) -> Result<String, ()> {
    if !info.active || !info.token_type.eq_ignore_ascii_case("bearer")
        || info.client_id != expected_client_id
        || info.account_id.len() != 32
        || !info.account_id.bytes().all(|byte| byte.is_ascii_hexdigit())
    {
        return Err(());
    }
    Ok(info.account_id.clone())
}

async fn read_limited(response: reqwest::Response) -> Result<Vec<u8>, ()> {
    if response.content_length().is_some_and(|length| length > MAX_PROVIDER_RESPONSE_BYTES as u64) {
        return Err(());
    }
    let mut response = response;
    let mut body = Vec::with_capacity(MAX_PROVIDER_RESPONSE_BYTES);
    while let Some(chunk) = response.chunk().await.map_err(|_| ())? {
        if body.len().saturating_add(chunk.len()) > MAX_PROVIDER_RESPONSE_BYTES {
            return Err(());
        }
        body.extend_from_slice(&chunk);
    }
    Ok(body)
}

fn valid_redirect_uri(value: &str) -> bool {
    let Ok(url) = Url::parse(value) else { return false };
    let loopback = url.host_str().is_some_and(|host| {
        host.eq_ignore_ascii_case("localhost") || host == "127.0.0.1" || host == "::1"
    });
    (url.scheme() == "https" || (url.scheme() == "http" && loopback))
        && url.host_str().is_some()
        && url.username().is_empty()
        && url.password().is_none()
        && url.query().is_none()
        && url.fragment().is_none()
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct AuthorizationUrlResponse {
    authorization_url: String,
}

pub(super) fn router() -> Router<PortalHttpState> {
    Router::new()
        .route("/account/links/epic/start", post(start))
        .route("/account/links/epic/callback", get(callback))
}

async fn start(State(state): State<PortalHttpState>, headers: HeaderMap) -> Response {
    let Some(provider) = state.epic_link.as_ref() else {
        return failure(StatusCode::SERVICE_UNAVAILABLE);
    };
    let (Some(session_token), Some(csrf)) = (
        cookie(&headers, "HogwartsRP.AccountSession"),
        headers.get("x-hogwartsrp-csrf").and_then(|value| value.to_str().ok()),
    ) else {
        return failure(StatusCode::UNAUTHORIZED);
    };
    if !is_token(&session_token) || !is_token(csrf)
        || !same_origin(&headers, state.config.callback_uri())
        || state.sessions.validate_csrf(&session_token, csrf, now()).is_err()
    {
        return failure(StatusCode::FORBIDDEN);
    }
    let (account_id, _) = match state.sessions.account_for(&session_token, now()) {
        Ok(value) => value,
        Err(_) => return failure(StatusCode::UNAUTHORIZED),
    };
    match state.account_links.statuses(&account_id) {
        Ok(statuses) if statuses.iter().any(|link| link.provider == AccountLinkProvider::Epic && link.connected) => {
            return failure(StatusCode::CONFLICT);
        }
        Ok(_) => {}
        Err(_) => return failure(StatusCode::INTERNAL_SERVER_ERROR),
    }

    // The shared transaction store generates a verifier for OIDC/Discord, but
    // Epic's documented web flow uses state and does not document PKCE.
    let transaction = match state.transactions.begin(&session_token, now()) {
        Ok(value) => value,
        Err(_) => return failure(StatusCode::SERVICE_UNAVAILABLE),
    };
    let authorization_url = match provider.authorization_url(transaction.state()) {
        Ok(value) => value,
        Err(_) => return failure(StatusCode::INTERNAL_SERVER_ERROR),
    };
    let mut response = Json(AuthorizationUrlResponse { authorization_url }).into_response();
    no_store(&mut response);
    response
}

async fn callback(
    State(state): State<PortalHttpState>,
    headers: HeaderMap,
    RawQuery(raw_query): RawQuery,
) -> Response {
    let Some(raw_query) = raw_query.filter(|query| query.len() <= 8192) else {
        return callback_redirect(false);
    };
    let mut values = BTreeMap::new();
    for (key, value) in form_urlencoded::parse(raw_query.as_bytes()) {
        if values.insert(key.into_owned(), value.into_owned()).is_some() {
            return callback_redirect(false);
        }
    }
    if values.keys().any(|key| !matches!(key.as_str(), "code" | "state" | "error" | "error_description")) {
        return callback_redirect(false);
    }
    if values.contains_key("error") {
        return callback_redirect(false);
    }
    let (Some(code), Some(callback_state)) = (values.get("code"), values.get("state")) else {
        return callback_redirect(false);
    };
    let Some(session_token) = cookie(&headers, "HogwartsRP.AccountSession") else {
        return callback_redirect(false);
    };
    if !is_token(&session_token) || !is_token(callback_state) {
        return callback_redirect(false);
    }
    if state.transactions.consume(callback_state, &session_token, now()).is_err() {
        return callback_redirect(false);
    }
    let Some(provider) = state.epic_link.as_ref() else {
        return callback_redirect(false);
    };
    let epic_account_id = match provider.exchange_and_verify(code).await {
        Ok(value) => value,
        Err(_) => return callback_redirect(false),
    };
    let (account_id, _) = match state.sessions.account_for(&session_token, now()) {
        Ok(value) => value,
        Err(_) => return callback_redirect(false),
    };
    match state.account_links.link_verified_identity(&account_id, AccountLinkProvider::Epic, &epic_account_id) {
        Ok(()) => callback_redirect(true),
        Err(_) => callback_redirect(false),
    }
}

fn callback_redirect(connected: bool) -> Response {
    let destination = if connected { "/account?link=epic_connected" } else { "/account?link_error=epic" };
    let mut response = Redirect::to(destination).into_response();
    no_store(&mut response);
    response
}

fn cookie(headers: &HeaderMap, name: &str) -> Option<String> {
    let raw = headers.get(header::COOKIE)?.to_str().ok()?;
    let mut found = None;
    for part in raw.split(';') {
        let Some((key, value)) = part.trim().split_once('=') else { continue };
        if key == name {
            if found.is_some() || value.is_empty() { return None; }
            found = Some(value.to_owned());
        }
    }
    found
}

fn is_token(value: &str) -> bool {
    value.len() == 43 && value.bytes().all(|byte| byte.is_ascii_alphanumeric() || byte == b'-' || byte == b'_')
}

fn same_origin(headers: &HeaderMap, callback_uri: &str) -> bool {
    let Some(origin) = headers.get(header::ORIGIN).and_then(|value| value.to_str().ok()) else { return false };
    let (Ok(request), Ok(callback)) = (Url::parse(origin), Url::parse(callback_uri)) else { return false };
    request.username().is_empty() && request.password().is_none()
        && request.path() == "/" && request.query().is_none() && request.fragment().is_none()
        && request.origin() == callback.origin()
}

fn failure(status: StatusCode) -> Response {
    let mut response = (status, "The account request could not be completed.").into_response();
    no_store(&mut response);
    response
}

fn no_store(response: &mut Response) {
    response.headers_mut().insert(header::CACHE_CONTROL, HeaderValue::from_static("no-store"));
    response.headers_mut().insert(header::REFERRER_POLICY, HeaderValue::from_static("no-referrer"));
}

fn now() -> u64 {
    std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap_or_default().as_secs()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn redirect_uri_requires_https_except_loopback_development() {
        assert!(valid_redirect_uri("https://accounts.example.test/account/links/epic/callback"));
        assert!(valid_redirect_uri("http://127.0.0.1:8787/account/links/epic/callback"));
        assert!(!valid_redirect_uri("http://accounts.example.test/account/links/epic/callback"));
        assert!(!valid_redirect_uri("https://user:pass@accounts.example.test/account/links/epic/callback"));
        assert!(!valid_redirect_uri("https://accounts.example.test/account/links/epic/callback?state=x"));
    }

    #[test]
    fn authorization_url_uses_fixed_epic_endpoint_minimal_scope_and_state() {
        let config = EpicLinkConfig {
            client_id: "epic-client".into(),
            client_secret: "server-secret".into(),
            deployment_id: "deployment".into(),
            redirect_uri: "https://accounts.example.test/account/links/epic/callback".into(),
        };
        let url = Url::parse(&config.authorization_url("session-state").unwrap()).unwrap();
        let query: BTreeMap<_, _> = url.query_pairs().into_owned().collect();
        assert_eq!(url.origin().ascii_serialization(), "https://www.epicgames.com");
        assert_eq!(query.get("response_type").map(String::as_str), Some("code"));
        assert_eq!(query.get("scope").map(String::as_str), Some("basic_profile"));
        assert_eq!(query.get("state").map(String::as_str), Some("session-state"));
        assert_eq!(query.get("client_id").map(String::as_str), Some("epic-client"));
        assert!(!query.contains_key("code_challenge"));
        assert!(!query.contains_key("client_secret"));
    }

    #[test]
    fn only_active_bearer_token_for_configured_client_and_hex_account_is_accepted() {
        let valid = EpicTokenInfo {
            active: true,
            token_type: "bearer".into(),
            client_id: "expected-client".into(),
            account_id: "0123456789abcdef0123456789abcdef".into(),
        };
        assert_eq!(
            verified_epic_account_id(&valid, "expected-client").unwrap(),
            "0123456789abcdef0123456789abcdef"
        );
        assert!(verified_epic_account_id(&valid, "other-client").is_err());
        let inactive = EpicTokenInfo { active: false, ..valid };
        assert!(verified_epic_account_id(&inactive, "expected-client").is_err());
    }

    #[test]
    fn rejects_malformed_epic_account_ids() {
        for account_id in ["", "short", "0123456789abcdef0123456789abcdeZ", "0123456789abcdef0123456789abcde "] {
            let info = EpicTokenInfo {
                active: true,
                token_type: "bearer".into(),
                client_id: "client".into(),
                account_id: account_id.into(),
            };
            assert!(verified_epic_account_id(&info, "client").is_err());
        }
    }
}
