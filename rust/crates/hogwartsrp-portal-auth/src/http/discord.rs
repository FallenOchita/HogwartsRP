use super::super::{AccountLinkProvider, PortalHttpState};
use axum::{
    extract::{Query, State},
    http::{header, HeaderMap, HeaderValue, StatusCode},
    response::{IntoResponse, Redirect, Response},
    routing::{get, post},
    Json, Router,
};
use serde::{Deserialize, Serialize};
use std::{env, time::Duration};
use url::Url;

const DISCORD_AUTHORIZE_ENDPOINT: &str = "https://discord.com/oauth2/authorize";
const DISCORD_TOKEN_ENDPOINT: &str = "https://discord.com/api/oauth2/token";
const DISCORD_CURRENT_USER_ENDPOINT: &str = "https://discord.com/api/users/@me";
const PROVIDER_HTTP_TIMEOUT: Duration = Duration::from_secs(8);
const MAX_PROVIDER_RESPONSE_BYTES: usize = 32 * 1024;

#[derive(Clone)]
pub struct DiscordLinkConfig {
    client_id: String,
    client_secret: String,
    redirect_uri: String,
}

impl DiscordLinkConfig {
    /// Read optional server-only Discord OAuth settings. Partial configuration
    /// is rejected rather than silently disabling a misconfigured provider.
    pub fn from_env() -> Result<Option<Self>, String> {
        let client_id = env::var("HOGWARTSRP_DISCORD_CLIENT_ID").ok();
        let client_secret = env::var("HOGWARTSRP_DISCORD_CLIENT_SECRET").ok();
        let redirect_uri = env::var("HOGWARTSRP_DISCORD_REDIRECT_URI").ok();
        if client_id.is_none() && client_secret.is_none() && redirect_uri.is_none() {
            return Ok(None);
        }
        let (Some(client_id), Some(client_secret), Some(redirect_uri)) =
            (client_id, client_secret, redirect_uri)
        else {
            return Err("Discord account linking requires client ID, client secret, and redirect URI.".into());
        };
        if client_id.trim().is_empty()
            || client_id.len() > 256
            || client_secret.trim().is_empty()
            || client_secret.len() > 1024
            || !valid_redirect_uri(&redirect_uri)
        {
            return Err("Discord account-link configuration is invalid.".into());
        }
        Ok(Some(Self { client_id, client_secret, redirect_uri }))
    }

    fn authorization_url(&self, state: &str, challenge: &str) -> Result<String, ()> {
        let mut url = Url::parse(DISCORD_AUTHORIZE_ENDPOINT).map_err(|_| ())?;
        url.query_pairs_mut()
            .append_pair("client_id", &self.client_id)
            .append_pair("redirect_uri", &self.redirect_uri)
            .append_pair("response_type", "code")
            .append_pair("scope", "identify")
            .append_pair("state", state)
            .append_pair("code_challenge", challenge)
            .append_pair("code_challenge_method", "S256");
        Ok(url.into())
    }
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

#[derive(Deserialize)]
struct DiscordCallbackQuery {
    code: Option<String>,
    state: Option<String>,
    error: Option<String>,
}

#[derive(Deserialize)]
struct DiscordTokenResponse {
    access_token: String,
    token_type: String,
}

#[derive(Deserialize)]
struct DiscordCurrentUser {
    id: String,
}

pub(super) fn router() -> Router<PortalHttpState> {
    Router::new()
        .route("/account/links/discord/start", post(start))
        .route("/account/links/discord/callback", get(callback))
}

async fn start(State(state): State<PortalHttpState>, headers: HeaderMap) -> Response {
    let Some(provider) = state.discord_link.as_ref() else {
        return failure(StatusCode::SERVICE_UNAVAILABLE);
    };
    let (Some(session_token), Some(csrf)) = (
        cookie(&headers, "HogwartsRP.AccountSession"),
        headers.get("x-hogwartsrp-csrf").and_then(|v| v.to_str().ok()),
    ) else {
        return failure(StatusCode::UNAUTHORIZED);
    };
    if !is_token(&session_token)
        || !is_token(csrf)
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
        Ok(statuses) if statuses.iter().any(|link| link.provider == AccountLinkProvider::Discord && link.connected) => {
            return failure(StatusCode::CONFLICT);
        }
        Ok(_) => {}
        Err(_) => return failure(StatusCode::INTERNAL_SERVER_ERROR),
    }

    let transaction = match state.transactions.begin(&session_token, now()) {
        Ok(value) => value,
        Err(_) => return failure(StatusCode::SERVICE_UNAVAILABLE),
    };
    let authorization_url = match provider.authorization_url(transaction.state(), transaction.code_challenge()) {
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
    Query(query): Query<DiscordCallbackQuery>,
) -> Response {
    if query.error.is_some() {
        return callback_redirect(false);
    }
    let (Some(code), Some(callback_state)) = (query.code, query.state) else {
        return Redirect::to("/account?link_error=discord").into_response();
    };
    let Some(session_token) = cookie(&headers, "HogwartsRP.AccountSession") else {
        return Redirect::to("/account?link_error=discord").into_response();
    };
    if !is_token(&session_token) || code.trim().is_empty() || code.len() > 4096 {
        return Redirect::to("/account?link_error=discord").into_response();
    }
    let transaction = match state.transactions.consume(&callback_state, &session_token, now()) {
        Ok(value) => value,
        Err(_) => return Redirect::to("/account?link_error=discord").into_response(),
    };
    let Some(provider) = state.discord_link.as_ref() else {
        return Redirect::to("/account?link_error=discord").into_response();
    };
    let external_subject = match verify_discord_identity(provider, &code, transaction.code_verifier()).await {
        Ok(value) => value,
        Err(_) => return Redirect::to("/account?link_error=discord").into_response(),
    };
    let (account_id, _) = match state.sessions.account_for(&session_token, now()) {
        Ok(value) => value,
        Err(_) => return Redirect::to("/account?link_error=discord").into_response(),
    };
    match state.account_links.link_verified_identity(&account_id, AccountLinkProvider::Discord, &external_subject) {
        Ok(()) => callback_redirect(true),
        Err(_) => Redirect::to("/account?link_error=discord").into_response(),
    }
}

async fn verify_discord_identity(
    config: &DiscordLinkConfig,
    code: &str,
    verifier: &str,
) -> Result<String, ()> {
    let client = reqwest::Client::builder()
        .redirect(reqwest::redirect::Policy::none())
        .timeout(PROVIDER_HTTP_TIMEOUT)
        .build()
        .map_err(|_| ())?;
    let token_response = client
        .post(DISCORD_TOKEN_ENDPOINT)
        .form(&[
            ("client_id", config.client_id.as_str()),
            ("client_secret", config.client_secret.as_str()),
            ("grant_type", "authorization_code"),
            ("code", code),
            ("redirect_uri", config.redirect_uri.as_str()),
            ("code_verifier", verifier),
        ])
        .send()
        .await
        .map_err(|_| ())?;
    let token_bytes = bounded_body(token_response).await?;
    let token: DiscordTokenResponse = serde_json::from_slice(&token_bytes).map_err(|_| ())?;
    if !token.token_type.eq_ignore_ascii_case("bearer")
        || token.access_token.is_empty()
        || token.access_token.len() > 8192
    {
        return Err(());
    }

    let user_response = client
        .get(DISCORD_CURRENT_USER_ENDPOINT)
        .bearer_auth(&token.access_token)
        .send()
        .await
        .map_err(|_| ())?;
    let user_bytes = bounded_body(user_response).await?;
    let user: DiscordCurrentUser = serde_json::from_slice(&user_bytes).map_err(|_| ())?;
    if !valid_snowflake(&user.id) {
        return Err(());
    }
    Ok(user.id)
}

async fn bounded_body(response: reqwest::Response) -> Result<Vec<u8>, ()> {
    if !response.status().is_success()
        || response.content_length().is_some_and(|length| length > MAX_PROVIDER_RESPONSE_BYTES as u64)
    {
        return Err(());
    }
    let bytes = response.bytes().await.map_err(|_| ())?;
    if bytes.len() > MAX_PROVIDER_RESPONSE_BYTES {
        return Err(());
    }
    Ok(bytes.to_vec())
}

fn callback_redirect(connected: bool) -> Response {
    let destination = if connected {
        "/account?link=discord_connected"
    } else {
        "/account?link_error=discord"
    };
    let mut response = Redirect::to(destination).into_response();
    no_store(&mut response);
    response
}

fn cookie(headers: &HeaderMap, name: &str) -> Option<String> {
    let raw = headers.get(header::COOKIE)?.to_str().ok()?;
    let mut found = None;
    for part in raw.split(';') {
        let (key, value) = part.trim().split_once('=')?;
        if key == name {
            if found.is_some() || value.is_empty() { return None; }
            found = Some(value.to_owned());
        }
    }
    found
}

fn is_token(value: &str) -> bool {
    value.len() == 43 && value.bytes().all(|b| b.is_ascii_alphanumeric() || b == b'-' || b == b'_')
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
    fn discord_redirect_requires_https_except_loopback_development() {
        assert!(valid_redirect_uri("https://accounts.example.test/account/links/discord/callback"));
        assert!(valid_redirect_uri("http://127.0.0.1:8787/account/links/discord/callback"));
        assert!(!valid_redirect_uri("http://accounts.example.test/callback"));
        assert!(!valid_redirect_uri("https://user:pass@accounts.example.test/callback"));
        assert!(!valid_redirect_uri("https://accounts.example.test/callback?next=evil"));
    }

    #[test]
    fn discord_authorization_url_uses_identify_and_s256_pkce() {
        let config = DiscordLinkConfig {
            client_id: "client-123".into(),
            client_secret: "server-secret".into(),
            redirect_uri: "https://accounts.example.test/account/links/discord/callback".into(),
        };
        let url = Url::parse(&config.authorization_url("state-value", "challenge-value").unwrap()).unwrap();
        let query: std::collections::HashMap<_, _> = url.query_pairs().into_owned().collect();
        assert_eq!(query.get("scope").map(String::as_str), Some("identify"));
        assert_eq!(query.get("code_challenge_method").map(String::as_str), Some("S256"));
        assert_eq!(query.get("state").map(String::as_str), Some("state-value"));
        assert!(!query.contains_key("client_secret"));
    }

    #[test]
    fn discord_identity_must_be_a_numeric_snowflake() {
        assert!(valid_snowflake("123456789012345678"));
        assert!(!valid_snowflake(""));
        assert!(!valid_snowflake("abc123"));
        assert!(!valid_snowflake("123456789012345678901"));
    }

    fn valid_snowflake(value: &str) -> bool {
        !value.is_empty() && value.len() <= 20 && value.bytes().all(|byte| byte.is_ascii_digit())
    }
}
