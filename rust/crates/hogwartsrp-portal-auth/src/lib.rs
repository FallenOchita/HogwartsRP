//! Configuration and provider discovery for the central HogwartsRP account portal.
//!
//! This crate deliberately does not share the per-game-server join-ticket OIDC flow.
//! It discovers one operator-configured identity provider for browser account sign-in.

use openidconnect::{core::CoreProviderMetadata, IssuerUrl};
use serde::Deserialize;
use std::time::Duration;
use thiserror::Error;
use url::Url;

const DISCOVERY_TIMEOUT: Duration = Duration::from_secs(8);

/// Deserialized central-portal OIDC settings.
///
/// The client secret is intentionally private and omitted from Debug/Serialize output.
#[derive(Clone, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct PortalOidcConfig {
    authority: String,
    client_id: String,
    #[serde(default)]
    client_secret: Option<String>,
    callback_uri: String,
    #[serde(default = "default_scopes")]
    scopes: Vec<String>,
    #[serde(default)]
    allow_loopback_http: bool,
}

fn default_scopes() -> Vec<String> {
    vec!["openid".to_owned(), "profile".to_owned()]
}

#[derive(Debug, Error, PartialEq, Eq)]
pub enum PortalOidcConfigError {
    #[error("OIDC authority must be an absolute HTTPS URL (loopback HTTP is development-only).")]
    InvalidAuthority,
    #[error("OIDC callback URI must be an absolute HTTPS URL (loopback HTTP is development-only).")]
    InvalidCallback,
    #[error("OIDC client ID must contain 1–256 non-whitespace characters.")]
    InvalidClientId,
    #[error("OIDC scopes must include the openid scope.")]
    MissingOpenIdScope,
    #[error("OIDC scopes must be non-empty, unique, and contain no whitespace.")]
    InvalidScopes,
}

#[derive(Debug, Error)]
pub enum PortalOidcDiscoveryError {
    #[error(transparent)]
    InvalidConfiguration(#[from] PortalOidcConfigError),
    #[error("Could not construct the OIDC discovery HTTP client: {0}")]
    HttpClient(String),
    #[error("OIDC provider discovery failed: {0}")]
    Discovery(String),
}

impl PortalOidcConfig {
    /// Validate configuration before any network request is made.
    pub fn validate(&self) -> Result<(), PortalOidcConfigError> {
        if self.client_id.trim().is_empty()
            || self.client_id.len() > 256
            || self.client_id.chars().any(char::is_whitespace)
        {
            return Err(PortalOidcConfigError::InvalidClientId);
        }

        validate_endpoint(&self.authority, self.allow_loopback_http)
            .map_err(|_| PortalOidcConfigError::InvalidAuthority)?;
        validate_endpoint(&self.callback_uri, self.allow_loopback_http)
            .map_err(|_| PortalOidcConfigError::InvalidCallback)?;

        if self.scopes.is_empty()
            || self.scopes.iter().any(|scope| {
                scope.trim().is_empty() || scope.chars().any(char::is_whitespace)
            })
        {
            return Err(PortalOidcConfigError::InvalidScopes);
        }
        let mut unique = self.scopes.clone();
        unique.sort();
        unique.dedup();
        if unique.len() != self.scopes.len() {
            return Err(PortalOidcConfigError::InvalidScopes);
        }
        if !self.scopes.iter().any(|scope| scope == "openid") {
            return Err(PortalOidcConfigError::MissingOpenIdScope);
        }

        Ok(())
    }

    pub fn authority(&self) -> &str {
        &self.authority
    }

    pub fn client_id(&self) -> &str {
        &self.client_id
    }

    pub fn callback_uri(&self) -> &str {
        &self.callback_uri
    }

    pub fn scopes(&self) -> &[String] {
        &self.scopes
    }

    pub fn has_client_secret(&self) -> bool {
        self.client_secret.as_deref().is_some_and(|value| !value.is_empty())
    }

    /// Discover endpoints from the configured issuer. Redirects are disabled and
    /// the request has a fixed timeout so a provider cannot redirect discovery.
    ///
    /// The openidconnect library validates that the discovered issuer matches the
    /// issuer URL supplied here. Callers must still validate tokens and claims
    /// before creating a HogwartsRP account.
    pub async fn discover_provider_metadata(
        &self,
    ) -> Result<CoreProviderMetadata, PortalOidcDiscoveryError> {
        self.validate()?;
        let issuer = IssuerUrl::new(self.authority.clone())
            .map_err(|error| PortalOidcDiscoveryError::Discovery(error.to_string()))?;
        let http_client = reqwest::Client::builder()
            .redirect(reqwest::redirect::Policy::none())
            .timeout(DISCOVERY_TIMEOUT)
            .build()
            .map_err(|error| PortalOidcDiscoveryError::HttpClient(error.to_string()))?;

        CoreProviderMetadata::discover_async(issuer, &http_client)
            .await
            .map_err(|error| PortalOidcDiscoveryError::Discovery(error.to_string()))
    }
}

fn validate_endpoint(value: &str, allow_loopback_http: bool) -> Result<Url, ()> {
    let url = Url::parse(value).map_err(|_| ())?;
    if url.host_str().is_none()
        || !url.username().is_empty()
        || url.password().is_some()
        || url.query().is_some()
        || url.fragment().is_some()
    {
        return Err(());
    }

    let secure = url.scheme() == "https";
    let loopback_http = allow_loopback_http
        && url.scheme() == "http"
        && url.host_str().is_some_and(is_loopback_host);
    if secure || loopback_http {
        Ok(url)
    } else {
        Err(())
    }
}

fn is_loopback_host(host: &str) -> bool {
    host.eq_ignore_ascii_case("localhost")
        || host == "127.0.0.1"
        || host == "::1"
}

#[cfg(test)]
mod tests {
    use super::*;

    fn config() -> PortalOidcConfig {
        PortalOidcConfig {
            authority: "https://identity.example.test/".into(),
            client_id: "hogwartsrp-portal".into(),
            client_secret: None,
            callback_uri: "https://accounts.example.test/account/signin-oidc".into(),
            scopes: default_scopes(),
            allow_loopback_http: false,
        }
    }

    #[test]
    fn accepts_valid_https_configuration() {
        assert_eq!(config().validate(), Ok(()));
    }

    #[test]
    fn rejects_http_authority_outside_explicit_loopback_development() {
        let mut value = config();
        value.authority = "http://identity.example.test".into();
        assert_eq!(
            value.validate(),
            Err(PortalOidcConfigError::InvalidAuthority)
        );
    }

    #[test]
    fn permits_loopback_http_only_when_explicitly_enabled() {
        let mut value = config();
        value.authority = "http://127.0.0.1:9000".into();
        value.callback_uri = "http://localhost:9000/account/signin-oidc".into();
        value.allow_loopback_http = true;
        assert_eq!(value.validate(), Ok(()));
    }

    #[test]
    fn rejects_non_loopback_http_even_in_development_mode() {
        let mut value = config();
        value.authority = "http://identity.example.test".into();
        value.allow_loopback_http = true;
        assert_eq!(
            value.validate(),
            Err(PortalOidcConfigError::InvalidAuthority)
        );
    }

    #[test]
    fn rejects_callback_with_userinfo_or_fragment() {
        let mut value = config();
        value.callback_uri = "https://user:pass@accounts.example.test/callback".into();
        assert_eq!(
            value.validate(),
            Err(PortalOidcConfigError::InvalidCallback)
        );

        value.callback_uri = "https://accounts.example.test/callback#fragment".into();
        assert_eq!(
            value.validate(),
            Err(PortalOidcConfigError::InvalidCallback)
        );
    }

    #[test]
    fn requires_openid_scope() {
        let mut value = config();
        value.scopes = vec!["profile".into()];
        assert_eq!(
            value.validate(),
            Err(PortalOidcConfigError::MissingOpenIdScope)
        );
    }

    #[test]
    fn rejects_duplicate_or_malformed_scopes() {
        let mut value = config();
        value.scopes = vec!["openid".into(), "openid".into()];
        assert_eq!(
            value.validate(),
            Err(PortalOidcConfigError::InvalidScopes)
        );

        value.scopes = vec!["openid profile".into()];
        assert_eq!(
            value.validate(),
            Err(PortalOidcConfigError::InvalidScopes)
        );
    }

    #[test]
    fn client_secret_is_not_exposed_by_public_accessors() {
        let mut value = config();
        value.client_secret = Some("server-only-secret".into());
        assert!(value.has_client_secret());
        assert_eq!(value.client_id(), "hogwartsrp-portal");
    }
}


/// Lifetime for a browser sign-in transaction.
const AUTHORIZATION_TRANSACTION_TTL_SECONDS: u64 = 300;
const DEFAULT_TRANSACTION_CAPACITY: usize = 4096;

/// Public values safe to use when redirecting the browser to the OIDC provider.
/// The PKCE verifier and nonce remain exclusively on the server.
#[derive(Clone, PartialEq, Eq, Debug)]
pub struct AuthorizationRequest {
    state: String,
    code_challenge: String,
    nonce: String,
}

impl AuthorizationRequest {
    pub fn state(&self) -> &str { &self.state }
    pub fn code_challenge(&self) -> &str { &self.code_challenge }
    pub fn nonce(&self) -> &str { &self.nonce }
    pub fn code_challenge_method(&self) -> &'static str { "S256" }
}

/// Server-only values recovered after a matching callback consumes the transaction.
/// Intentionally has no Debug implementation to avoid accidental secret logging.
pub struct ConsumedAuthorizationTransaction {
    nonce: String,
    code_verifier: String,
}

impl ConsumedAuthorizationTransaction {
    pub fn nonce(&self) -> &str { &self.nonce }
    pub fn code_verifier(&self) -> &str { &self.code_verifier }
}

impl std::fmt::Debug for ConsumedAuthorizationTransaction {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        formatter.debug_struct("ConsumedAuthorizationTransaction")
            .field("nonce", &"[REDACTED]")
            .field("code_verifier", &"[REDACTED]")
            .finish()
    }
}

struct PendingAuthorization {
    session_hash: [u8; 32],
    nonce: String,
    code_verifier: String,
    expires_at: u64,
}

/// Bounded, process-local, single-use OIDC transaction storage.
///
/// Production deployments with multiple portal instances must replace this with a
/// shared store that preserves atomic consume semantics.
pub struct AuthorizationTransactionStore {
    entries: std::sync::Mutex<std::collections::HashMap<[u8; 32], PendingAuthorization>>,
    capacity: usize,
}

#[derive(Debug, Error, PartialEq, Eq)]
pub enum AuthorizationTransactionError {
    #[error("The authorization transaction store is unavailable.")]
    Unavailable,
    #[error("The authorization transaction store is full.")]
    CapacityReached,
    #[error("The authorization transaction is invalid, expired, or already used.")]
    InvalidOrExpired,
}

impl Default for AuthorizationTransactionStore {
    fn default() -> Self { Self::new(DEFAULT_TRANSACTION_CAPACITY) }
}

impl AuthorizationTransactionStore {
    pub fn new(capacity: usize) -> Self {
        Self {
            entries: std::sync::Mutex::new(std::collections::HashMap::new()),
            capacity: capacity.max(1),
        }
    }

    /// Begin a sign-in attempt bound to the browser pre-authentication session identifier.
    /// The identifier itself is never retained; only its SHA-256 hash is stored.
    pub fn begin(
        &self,
        browser_session_id: &str,
        now_unix_seconds: u64,
    ) -> Result<AuthorizationRequest, AuthorizationTransactionError> {
        if browser_session_id.is_empty() {
            return Err(AuthorizationTransactionError::InvalidOrExpired);
        }

        use rand::RngCore;
        use sha2::{Digest, Sha256};
        use base64::{engine::general_purpose::URL_SAFE_NO_PAD, Engine as _};

        let mut state_bytes = [0u8; 32];
        let mut nonce_bytes = [0u8; 32];
        let mut verifier_bytes = [0u8; 32];
        let mut rng = rand::rngs::OsRng;
        rng.fill_bytes(&mut state_bytes);
        rng.fill_bytes(&mut nonce_bytes);
        rng.fill_bytes(&mut verifier_bytes);

        let state = URL_SAFE_NO_PAD.encode(state_bytes);
        let nonce = URL_SAFE_NO_PAD.encode(nonce_bytes);
        let code_verifier = URL_SAFE_NO_PAD.encode(verifier_bytes);
        let challenge = URL_SAFE_NO_PAD.encode(Sha256::digest(code_verifier.as_bytes()));
        let state_hash: [u8; 32] = Sha256::digest(state.as_bytes()).into();
        let session_hash: [u8; 32] = Sha256::digest(browser_session_id.as_bytes()).into();

        let mut entries = self.entries.lock()
            .map_err(|_| AuthorizationTransactionError::Unavailable)?;
        entries.retain(|_, transaction| transaction.expires_at > now_unix_seconds);
        if entries.len() >= self.capacity {
            return Err(AuthorizationTransactionError::CapacityReached);
        }

        entries.insert(state_hash, PendingAuthorization {
            session_hash,
            nonce: nonce.clone(),
            code_verifier,
            expires_at: now_unix_seconds.saturating_add(AUTHORIZATION_TRANSACTION_TTL_SECONDS),
        });

        Ok(AuthorizationRequest { state, code_challenge: challenge, nonce })
    }

    /// Atomically consume a callback transaction. Wrong-session attempts do not
    /// consume the rightful user's transaction.
    pub fn consume(
        &self,
        state: &str,
        browser_session_id: &str,
        now_unix_seconds: u64,
    ) -> Result<ConsumedAuthorizationTransaction, AuthorizationTransactionError> {
        use sha2::{Digest, Sha256};
        let state_hash: [u8; 32] = Sha256::digest(state.as_bytes()).into();
        let session_hash: [u8; 32] = Sha256::digest(browser_session_id.as_bytes()).into();
        let mut entries = self.entries.lock()
            .map_err(|_| AuthorizationTransactionError::Unavailable)?;

        let Some(pending) = entries.get(&state_hash) else {
            return Err(AuthorizationTransactionError::InvalidOrExpired);
        };
        if pending.expires_at <= now_unix_seconds {
            entries.remove(&state_hash);
            return Err(AuthorizationTransactionError::InvalidOrExpired);
        }
        if pending.session_hash != session_hash {
            return Err(AuthorizationTransactionError::InvalidOrExpired);
        }

        let pending = entries.remove(&state_hash)
            .ok_or(AuthorizationTransactionError::InvalidOrExpired)?;
        Ok(ConsumedAuthorizationTransaction {
            nonce: pending.nonce,
            code_verifier: pending.code_verifier,
        })
    }

    pub fn len(&self) -> Result<usize, AuthorizationTransactionError> {
        self.entries.lock()
            .map(|entries| entries.len())
            .map_err(|_| AuthorizationTransactionError::Unavailable)
    }
}


#[cfg(test)]
mod transaction_tests {
    use super::*;

    #[test]
    fn creates_s256_challenge_and_keeps_verifier_server_side() {
        let store = AuthorizationTransactionStore::default();
        let request = store.begin("browser-session-a", 1_000).unwrap();
        assert_eq!(request.code_challenge_method(), "S256");
        assert_eq!(request.state().len(), 43);
        assert_eq!(request.code_challenge().len(), 43);
        assert_eq!(request.nonce().len(), 43);
        let consumed = store.consume(request.state(), "browser-session-a", 1_001).unwrap();
        assert_eq!(consumed.code_verifier().len(), 43);
        assert_eq!(store.len().unwrap(), 0);
    }

    #[test]
    fn transaction_is_bound_to_its_browser_session() {
        let store = AuthorizationTransactionStore::default();
        let request = store.begin("owner-session", 1_000).unwrap();
        assert_eq!(
            store.consume(request.state(), "different-session", 1_001).unwrap_err(),
            AuthorizationTransactionError::InvalidOrExpired
        );
        assert_eq!(store.len().unwrap(), 1);
        assert!(store.consume(request.state(), "owner-session", 1_001).is_ok());
    }

    #[test]
    fn transaction_is_single_use() {
        let store = AuthorizationTransactionStore::default();
        let request = store.begin("session", 1_000).unwrap();
        assert!(store.consume(request.state(), "session", 1_001).is_ok());
        assert_eq!(
            store.consume(request.state(), "session", 1_002).unwrap_err(),
            AuthorizationTransactionError::InvalidOrExpired
        );
    }

    #[test]
    fn expired_transactions_are_rejected_and_cleaned() {
        let store = AuthorizationTransactionStore::default();
        let request = store.begin("session", 1_000).unwrap();
        assert_eq!(
            store.consume(request.state(), "session", 1_300).unwrap_err(),
            AuthorizationTransactionError::InvalidOrExpired
        );
        assert_eq!(store.len().unwrap(), 0);
    }

    #[test]
    fn store_is_bounded_and_prunes_expired_entries_before_capacity_check() {
        let store = AuthorizationTransactionStore::new(1);
        let first = store.begin("session-a", 1_000).unwrap();
        assert_eq!(
            store.begin("session-b", 1_001).unwrap_err(),
            AuthorizationTransactionError::CapacityReached
        );
        let second = store.begin("session-b", 1_301).unwrap();
        assert_ne!(first.state(), second.state());
        assert_eq!(store.len().unwrap(), 1);
    }

    #[test]
    fn empty_session_binding_is_rejected() {
        let store = AuthorizationTransactionStore::default();
        assert_eq!(
            store.begin("", 1_000).unwrap_err(),
            AuthorizationTransactionError::InvalidOrExpired
        );
    }
}

mod authentication;
pub use authentication::{PortalOidcAuthenticationError, VerifiedPortalIdentity};

mod accounts;
mod account_links;
mod sessions;
mod http;
pub use accounts::{JsonPortalAccountStore, PortalAccountStoreError};
pub use account_links::{AccountLinkProvider, AccountLinkStatus, AccountLinkStoreError, JsonAccountLinkStore};
pub use sessions::{BrowserSessionError, BrowserSessionStore, IssuedBrowserSession};
pub use http::{router as portal_router, DiscordLinkConfig, EpicLinkConfig, PortalHttpState, SteamLinkConfig};
