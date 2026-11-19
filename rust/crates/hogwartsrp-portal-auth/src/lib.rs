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
