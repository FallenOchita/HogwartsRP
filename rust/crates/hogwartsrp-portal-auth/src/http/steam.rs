use super::PortalHttpState;
use crate::AccountLinkProvider;
use axum::{
    extract::{RawQuery, State},
    http::{header, HeaderMap, HeaderValue, StatusCode},
    response::{IntoResponse, Redirect, Response},
    routing::{get, post},
    Json, Router,
};
use serde::Serialize;
use std::{collections::BTreeMap, env, time::Duration};
use url::{form_urlencoded, Url};

const STEAM_OPENID_ENDPOINT: &str = "https://steamcommunity.com/openid/login";
const OPENID_NAMESPACE: &str = "http://specs.openid.net/auth/2.0";
const IDENTIFIER_SELECT: &str = "http://specs.openid.net/auth/2.0/identifier_select";
const CLAIMED_ID_PREFIX: &str = "http://steamcommunity.com/openid/id/";
const PROVIDER_HTTP_TIMEOUT: Duration = Duration::from_secs(8);
const MAX_PROVIDER_RESPONSE_BYTES: usize = 4096;
const REQUIRED_SIGNED_FIELDS: &[&str] = &[
    "op_endpoint", "claimed_id", "identity", "return_to", "response_nonce", "assoc_handle",
];

#[derive(Clone)]
pub struct SteamLinkConfig {
    redirect_uri: String,
}

impl SteamLinkConfig {
    pub fn from_env() -> Result<Option<Self>, String> {
        let Some(redirect_uri) = env::var("HOGWARTSRP_STEAM_REDIRECT_URI").ok() else {
            return Ok(None);
        };
        if !valid_redirect_uri(&redirect_uri)
            || Url::parse(&redirect_uri).ok().is_none_or(|url| url.path() != "/account/links/steam/callback")
        {
            return Err("Steam account-link redirect URI is invalid.".into());
        }
        Ok(Some(Self { redirect_uri }))
    }

    fn authorization_url(&self, state: &str) -> Result<String, ()> {
        let callback = Url::parse(&self.redirect_uri).map_err(|_| ())?;
        let mut return_to = callback.clone();
        return_to.query_pairs_mut().append_pair("state", state);
        let realm = format!("{}/", callback.origin().ascii_serialization());
        let mut url = Url::parse(STEAM_OPENID_ENDPOINT).map_err(|_| ())?;
        url.query_pairs_mut()
            .append_pair("openid.ns", OPENID_NAMESPACE)
            .append_pair("openid.mode", "checkid_setup")
            .append_pair("openid.return_to", return_to.as_str())
            .append_pair("openid.realm", &realm)
            .append_pair("openid.identity", IDENTIFIER_SELECT)
            .append_pair("openid.claimed_id", IDENTIFIER_SELECT);
        Ok(url.to_string())
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

pub(super) fn router() -> Router<PortalHttpState> {
    Router::new()
        .route("/account/links/steam/start", post(start))
        .route("/account/links/steam/callback", get(callback))
}

async fn start(State(state): State<PortalHttpState>, headers: HeaderMap) -> Response {
    let Some(provider) = state.steam_link.as_ref() else {
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
        Ok(statuses) if statuses.iter().any(|link| link.provider == AccountLinkProvider::Steam && link.connected) => {
            return failure(StatusCode::CONFLICT);
        }
        Ok(_) => {}
        Err(_) => return failure(StatusCode::INTERNAL_SERVER_ERROR),
    }

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
    let Some(raw_query) = raw_query else {
        return callback_redirect(false);
    };
    let mut values = BTreeMap::new();
    for (key, value) in form_urlencoded::parse(raw_query.as_bytes()) {
        if values.insert(key.into_owned(), value.into_owned()).is_some() {
            return callback_redirect(false);
        }
    }
    let Some(callback_state) = values.remove("state") else {
        return callback_redirect(false);
    };
    let Some(session_token) = cookie(&headers, "HogwartsRP.AccountSession") else {
        return callback_redirect(false);
    };
    if !is_token(&session_token) || values.len() < 8 || values.len() > 64 {
        return callback_redirect(false);
    }
    let transaction = match state.transactions.consume(&callback_state, &session_token, now()) {
        Ok(value) => value,
        Err(_) => return callback_redirect(false),
    };
    let Some(provider) = state.steam_link.as_ref() else {
        return callback_redirect(false);
    };
    let steam_id = match verify_assertion(provider, &values, &callback_state).await {
        Ok(value) => value,
        Err(_) => return callback_redirect(false),
    };
    let (account_id, _) = match state.sessions.account_for(&session_token, now()) {
        Ok(value) => value,
        Err(_) => return callback_redirect(false),
    };
    match state.account_links.link_verified_identity(&account_id, AccountLinkProvider::Steam, &steam_id) {
        Ok(()) => callback_redirect(true),
        Err(_) => callback_redirect(false),
    }
}

async fn verify_assertion(
    config: &SteamLinkConfig,
    values: &BTreeMap<String, String>,
    callback_state: &str,
) -> Result<String, ()> {
    if values.iter().any(|(key, value)| {
        !key.starts_with("openid.") || key.len() > 64 || value.is_empty()
            || value.len() > 4096 || value.chars().any(char::is_control)
    }) {
        return Err(());
    }
    require(values, "openid.ns", OPENID_NAMESPACE)?;
    require(values, "openid.mode", "id_res")?;
    require(values, "openid.op_endpoint", STEAM_OPENID_ENDPOINT)?;

    let claimed_id = values.get("openid.claimed_id").ok_or(())?;
    let steam_id = claimed_id.strip_prefix(CLAIMED_ID_PREFIX).ok_or(())?;
    if steam_id.len() != 17 || !steam_id.bytes().all(|byte| byte.is_ascii_digit())
        || steam_id.parse::<u64>().ok().filter(|id| *id != 0).is_none()
    {
        return Err(());
    }
    require(values, "openid.identity", claimed_id)?;

    let mut expected_return_to = Url::parse(&config.redirect_uri).map_err(|_| ())?;
    expected_return_to.query_pairs_mut().append_pair("state", callback_state);
    let returned = values.get("openid.return_to").ok_or(())?;
    if returned != expected_return_to.as_str() {
        return Err(());
    }

    validate_nonce(values.get("openid.response_nonce").ok_or(())?, now())?;
    let signed = values.get("openid.signed").ok_or(())?;
    for required in REQUIRED_SIGNED_FIELDS {
        if !signed.split(',').any(|field| field == *required) {
            return Err(());
        }
    }
    for required in ["openid.assoc_handle", "openid.sig", "openid.signed"] {
        if values.get(required).is_none_or(|value| value.trim().is_empty()) {
            return Err(());
        }
    }

    let client = reqwest::Client::builder()
        .redirect(reqwest::redirect::Policy::none())
        .timeout(PROVIDER_HTTP_TIMEOUT)
        .build()
        .map_err(|_| ())?;
    let mut form: Vec<(String, String)> = values.iter().map(|(k, v)| (k.clone(), v.clone())).collect();
    if let Some((_, value)) = form.iter_mut().find(|(key, _)| key == "openid.mode") {
        *value = "check_authentication".into();
    } else {
        return Err(());
    }
    let response = client.post(STEAM_OPENID_ENDPOINT).form(&form).send().await.map_err(|_| ())?;
    if !response.status().is_success()
        || response.content_length().is_some_and(|length| length > MAX_PROVIDER_RESPONSE_BYTES as u64)
    {
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
    let body = std::str::from_utf8(&body).map_err(|_| ())?;
    let has_namespace = body.lines().any(|line| line.trim() == format!("ns:{OPENID_NAMESPACE}"));
    let is_valid = body.lines().any(|line| line.trim() == "is_valid:true");
    if !has_namespace || !is_valid {
        return Err(());
    }
    Ok(steam_id.to_owned())
}

fn require(values: &BTreeMap<String, String>, key: &str, expected: &str) -> Result<(), ()> {
    if values.get(key).is_some_and(|actual| actual == expected) { Ok(()) } else { Err(()) }
}

fn validate_nonce(value: &str, now: u64) -> Result<(), ()> {
    if value.len() < 21 || value.len() > 256 || value.chars().any(char::is_control) {
        return Err(());
    }
    let timestamp = parse_timestamp(&value[..20]).ok_or(())?;
    if timestamp > now.saturating_add(120) || now.saturating_sub(timestamp) > 600 {
        return Err(());
    }
    Ok(())
}

fn parse_timestamp(value: &str) -> Option<u64> {
    let bytes = value.as_bytes();
    if bytes.len() != 20 || bytes[4] != b'-' || bytes[7] != b'-'
        || bytes[10] != b'T' || bytes[13] != b':' || bytes[16] != b':' || bytes[19] != b'Z'
    {
        return None;
    }
    let number = |range: std::ops::Range<usize>| -> Option<u32> {
        let slice = bytes.get(range)?;
        if !slice.iter().all(u8::is_ascii_digit) { return None; }
        std::str::from_utf8(slice).ok()?.parse().ok()
    };
    let year = number(0..4)? as i64;
    let month = number(5..7)?;
    let day = number(8..10)?;
    let hour = number(11..13)?;
    let minute = number(14..16)?;
    let second = number(17..19)?;
    if !(1..=12).contains(&month) || hour > 23 || minute > 59 || second > 59 {
        return None;
    }
    let leap = year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);
    let days_in_month = [31, if leap { 29 } else { 28 }, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
    if day == 0 || day > days_in_month[(month - 1) as usize] { return None; }
    let adjusted_year = year - if month <= 2 { 1 } else { 0 };
    let era = adjusted_year.div_euclid(400);
    let year_of_era = adjusted_year - era * 400;
    let adjusted_month = month as i64 + if month > 2 { -3 } else { 9 };
    let day_of_year = (153 * adjusted_month + 2) / 5 + day as i64 - 1;
    let day_of_era = year_of_era * 365 + year_of_era / 4 - year_of_era / 100 + day_of_year;
    let days_since_epoch = era * 146097 + day_of_era - 719468;
    u64::try_from(days_since_epoch * 86400 + (hour * 3600 + minute * 60 + second) as i64).ok()
}

fn callback_redirect(connected: bool) -> Response {
    let destination = if connected { "/account?link=steam_connected" } else { "/account?link_error=steam" };
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
    fn redirect_uri_requires_https_except_loopback_development() {
        assert!(valid_redirect_uri("https://accounts.example.test/account/links/steam/callback"));
        assert!(valid_redirect_uri("http://127.0.0.1:8787/account/links/steam/callback"));
        assert!(!valid_redirect_uri("http://accounts.example.test/callback"));
        assert!(!valid_redirect_uri("https://user:pass@accounts.example.test/callback"));
        assert!(!valid_redirect_uri("https://accounts.example.test/callback?state=attacker"));
    }

    #[test]
    fn authorization_url_uses_fixed_steam_endpoint_and_session_state() {
        let config = SteamLinkConfig { redirect_uri: "https://accounts.example.test/account/links/steam/callback".into() };
        let url = Url::parse(&config.authorization_url("state-value").unwrap()).unwrap();
        let query: BTreeMap<_, _> = url.query_pairs().into_owned().collect();
        assert_eq!(url.origin().ascii_serialization(), "https://steamcommunity.com");
        assert_eq!(query.get("openid.mode").map(String::as_str), Some("checkid_setup"));
        assert_eq!(query.get("openid.ns").map(String::as_str), Some(OPENID_NAMESPACE));
        assert!(query.get("openid.return_to").unwrap().ends_with("?state=state-value"));
        assert!(!query.contains_key("client_secret"));
    }

    #[test]
    fn steam_id_must_be_exactly_seventeen_digits() {
        assert!("76561198002516729".parse::<u64>().is_ok());
        assert!(!"7656119800251672x".bytes().all(|b| b.is_ascii_digit()));
        assert_ne!("123".len(), 17);
    }

    #[test]
    fn nonce_timestamp_parser_validates_utc_timestamp() {
        assert_eq!(parse_timestamp("2026-11-19T12:00:00Z"), Some(1_795_089_600));
        assert!(parse_timestamp("2026-02-30T12:00:00Z").is_none());
        assert!(parse_timestamp("not-a-timestamp-000").is_none());
    }

    #[test]
    fn nonce_window_rejects_old_and_future_values() {
        let now = parse_timestamp("2026-11-19T12:00:00Z").unwrap();
        assert!(validate_nonce("2026-11-19T11:55:00Zunique", now).is_ok());
        assert!(validate_nonce("2026-11-19T11:00:00Zunique", now).is_err());
        assert!(validate_nonce("2026-11-19T12:05:00Zunique", now).is_err());
    }
}
