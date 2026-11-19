use crate::{
    AuthorizationTransactionStore, BrowserSessionStore, JsonPortalAccountStore,
    PortalOidcConfig,
};
use axum::{
    extract::{Query, State},
    http::{header, HeaderMap, HeaderValue, StatusCode},
    response::{IntoResponse, Redirect, Response},
    routing::get,
    Json, Router,
};
use openidconnect::{
    core::{CoreClient, CoreProviderMetadata},
    AuthenticationFlow, ClientId, ClientSecret, CsrfToken, Nonce, PkceCodeChallenge,
    Scope,
};
use rand::RngCore;
use serde::{Deserialize, Serialize};
use std::{sync::Arc, time::{SystemTime, UNIX_EPOCH}};

const PREAUTH_COOKIE: &str = "HogwartsRP.PortalPreAuth";
const SESSION_COOKIE: &str = "HogwartsRP.AccountSession";
const CSRF_COOKIE: &str = "HogwartsRP.AccountCsrf";
const SESSION_TTL: u64 = 12 * 60 * 60;

#[derive(Clone)]
pub struct PortalHttpState {
    config: Arc<PortalOidcConfig>,
    metadata: Arc<CoreProviderMetadata>,
    transactions: Arc<AuthorizationTransactionStore>,
    accounts: Arc<JsonPortalAccountStore>,
    sessions: Arc<BrowserSessionStore>,
}

impl PortalHttpState {
    pub fn new(
        config: PortalOidcConfig,
        metadata: CoreProviderMetadata,
        accounts: Arc<JsonPortalAccountStore>,
    ) -> Self {
        Self {
            config: Arc::new(config),
            metadata: Arc::new(metadata),
            transactions: Arc::new(AuthorizationTransactionStore::default()),
            accounts,
            sessions: Arc::new(BrowserSessionStore::default()),
        }
    }
}

pub fn router(state: PortalHttpState) -> Router {
    Router::new()
        .route("/account/login", get(login_start))
        .route("/account/callback", get(login_callback))
        .route("/account/session", get(session_info))
        .route("/account/logout", axum::routing::post(logout))
        .with_state(state)
}

#[derive(Deserialize)]
struct CallbackQuery {
    code: Option<String>,
    state: Option<String>,
    error: Option<String>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct SessionResponse {
    account_id: String,
    csrf_token: String,
    expires_at: u64,
}

async fn login_start(State(state): State<PortalHttpState>, headers: HeaderMap) -> Response {
    let preauth = cookie(&headers, PREAUTH_COOKIE).unwrap_or_else(random_token);
    let now = now();
    let transaction = match state.transactions.begin(&preauth, now) {
        Ok(value) => value,
        Err(_) => return generic_error(StatusCode::SERVICE_UNAVAILABLE),
    };

    let client = CoreClient::from_provider_metadata(
        (*state.metadata).clone(),
        ClientId::new(state.config.client_id().to_owned()),
        None::<ClientSecret>,
    );
    let mut request = client.authorize_url(
        AuthenticationFlow::AuthorizationCode,
        || CsrfToken::new(transaction.state().to_owned()),
        || Nonce::new(transaction.nonce().to_owned()),
    ).set_pkce_challenge(PkceCodeChallenge::new(transaction.code_challenge().to_owned()));
    for scope in state.config.scopes() {
        request = request.add_scope(Scope::new(scope.clone()));
    }
    let (url, _, _) = request.url();
    let mut response = Redirect::temporary(url.as_str()).into_response();
    set_cookie(&mut response, format!("{PREAUTH_COOKIE}={preauth}; Path=/account; HttpOnly; Secure; SameSite=Lax; Max-Age=300"));
    no_store(&mut response);
    response
}

async fn login_callback(
    State(state): State<PortalHttpState>,
    headers: HeaderMap,
    Query(query): Query<CallbackQuery>,
) -> Response {
    if query.error.is_some() { return generic_error(StatusCode::UNAUTHORIZED); }
    let (Some(code), Some(callback_state)) = (query.code, query.state) else {
        return generic_error(StatusCode::BAD_REQUEST);
    };
    if code.trim().is_empty() || callback_state.trim().is_empty() {
        return generic_error(StatusCode::BAD_REQUEST);
    }
    let Some(preauth) = cookie(&headers, PREAUTH_COOKIE) else {
        return generic_error(StatusCode::UNAUTHORIZED);
    };
    let transaction = match state.transactions.consume(&callback_state, &preauth, now()) {
        Ok(value) => value,
        Err(_) => return generic_error(StatusCode::UNAUTHORIZED),
    };
    let identity = match state.config.exchange_code_and_validate((*state.metadata).clone(), &code, &transaction).await {
        Ok(value) => value,
        Err(_) => return generic_error(StatusCode::UNAUTHORIZED),
    };
    let account_id = match state.accounts.resolve_or_create(identity.issuer(), identity.subject()) {
        Ok(value) => value,
        Err(_) => return generic_error(StatusCode::INTERNAL_SERVER_ERROR),
    };
    let issued = match state.sessions.issue(&account_id, now()) {
        Ok(value) => value,
        Err(_) => return generic_error(StatusCode::SERVICE_UNAVAILABLE),
    };
    let mut response = Redirect::to("/account").into_response();
    set_cookie(&mut response, format!("{SESSION_COOKIE}={}; Path=/account; HttpOnly; Secure; SameSite=Lax; Max-Age={SESSION_TTL}", issued.session_token));
    set_cookie(&mut response, format!("{CSRF_COOKIE}={}; Path=/account; Secure; SameSite=Lax; Max-Age={SESSION_TTL}", issued.csrf_token));
    set_cookie(&mut response, format!("{PREAUTH_COOKIE}=; Path=/account; HttpOnly; Secure; SameSite=Lax; Max-Age=0"));
    no_store(&mut response);
    response
}

async fn session_info(State(state): State<PortalHttpState>, headers: HeaderMap) -> Response {
    let Some(token) = cookie(&headers, SESSION_COOKIE) else {
        return generic_error(StatusCode::UNAUTHORIZED);
    };
    let Some(csrf) = cookie(&headers, CSRF_COOKIE) else {
        return generic_error(StatusCode::UNAUTHORIZED);
    };
    let (account_id, expires_at) = match state.sessions.account_for(&token, now()) {
        Ok(value) => value,
        Err(_) => return generic_error(StatusCode::UNAUTHORIZED),
    };
    if state.sessions.validate_csrf(&token, &csrf, now()).is_err() {
        return generic_error(StatusCode::UNAUTHORIZED);
    }
    let mut response = Json(SessionResponse { account_id, csrf_token: csrf, expires_at }).into_response();
    no_store(&mut response);
    response
}

async fn logout(State(state): State<PortalHttpState>, headers: HeaderMap) -> Response {
    let (Some(token), Some(csrf)) = (cookie(&headers, SESSION_COOKIE), headers.get("x-hogwartsrp-csrf").and_then(|v| v.to_str().ok())) else {
        return generic_error(StatusCode::UNAUTHORIZED);
    };
    if state.sessions.validate_csrf(&token, csrf, now()).is_err() {
        return generic_error(StatusCode::FORBIDDEN);
    }
    let _ = state.sessions.revoke(&token);
    let mut response = StatusCode::NO_CONTENT.into_response();
    set_cookie(&mut response, format!("{SESSION_COOKIE}=; Path=/account; HttpOnly; Secure; SameSite=Lax; Max-Age=0"));
    set_cookie(&mut response, format!("{CSRF_COOKIE}=; Path=/account; Secure; SameSite=Lax; Max-Age=0"));
    no_store(&mut response);
    response
}

fn cookie(headers: &HeaderMap, name: &str) -> Option<String> {
    headers.get(header::COOKIE)?.to_str().ok()?.split(';').find_map(|part| {
        let (key, value) = part.trim().split_once('=')?;
        (key == name && !value.is_empty()).then(|| value.to_owned())
    })
}

fn set_cookie(response: &mut Response, value: String) {
    if let Ok(header) = HeaderValue::from_str(&value) {
        response.headers_mut().append(header::SET_COOKIE, header);
    }
}

fn no_store(response: &mut Response) {
    response.headers_mut().insert(header::CACHE_CONTROL, HeaderValue::from_static("no-store"));
    response.headers_mut().insert(header::PRAGMA, HeaderValue::from_static("no-cache"));
    response.headers_mut().insert(header::REFERRER_POLICY, HeaderValue::from_static("no-referrer"));
}

fn generic_error(status: StatusCode) -> Response {
    let mut response = (status, "The account request could not be completed.").into_response();
    no_store(&mut response);
    response
}

fn random_token() -> String {
    use base64::{engine::general_purpose::URL_SAFE_NO_PAD, Engine as _};
    let mut bytes = [0u8; 32];
    rand::rngs::OsRng.fill_bytes(&mut bytes);
    URL_SAFE_NO_PAD.encode(bytes)
}

fn now() -> u64 {
    SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default().as_secs()
}
