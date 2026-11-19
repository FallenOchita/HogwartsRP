use super::{ConsumedAuthorizationTransaction, PortalOidcConfig};
use openidconnect::{
    core::{CoreClient, CoreProviderMetadata},
    AuthorizationCode, ClientId, ClientSecret, Nonce, PkceCodeVerifier, RedirectUrl,
};
use thiserror::Error;
use std::time::Duration;

const TOKEN_REQUEST_TIMEOUT: Duration = Duration::from_secs(10);

/// A stable identity accepted only after successful OIDC ID-token verification.
///
/// Email and display-name claims are deliberately excluded. Account keys should use
/// the exact issuer + subject pair, not an email address that can change.
#[derive(Clone, PartialEq, Eq)]
pub struct VerifiedPortalIdentity {
    issuer: String,
    subject: String,
}

impl VerifiedPortalIdentity {
    pub fn issuer(&self) -> &str {
        &self.issuer
    }

    pub fn subject(&self) -> &str {
        &self.subject
    }
}

impl std::fmt::Debug for VerifiedPortalIdentity {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        formatter
            .debug_struct("VerifiedPortalIdentity")
            .field("issuer", &self.issuer)
            .field("subject", &"[REDACTED]")
            .finish()
    }
}

#[derive(Debug, Error, PartialEq, Eq)]
pub enum PortalOidcAuthenticationError {
    #[error("The authorization code or OIDC transaction is invalid.")]
    InvalidRequest,
    #[error("The OIDC token endpoint request could not be prepared.")]
    ClientSetup,
    #[error("The OIDC authorization code exchange failed.")]
    CodeExchange,
    #[error("The provider did not return an ID token.")]
    MissingIdToken,
    #[error("The provider ID token failed cryptographic or claim validation.")]
    InvalidIdToken,
    #[error("The verified ID token did not contain a usable issuer and subject.")]
    InvalidIdentity,
}

impl PortalOidcConfig {
    /// Exchange a callback authorization code and validate the returned ID token.
    ///
    /// The caller must consume the state/session-bound transaction before calling
    /// this method. The OIDC library verifies the token signature using discovered
    /// JWKS, issuer, audience, expiry, and the transaction nonce. Only the verified
    /// issuer + subject pair is returned; no unverified profile data is trusted.
    pub async fn exchange_code_and_validate(
        &self,
        metadata: CoreProviderMetadata,
        authorization_code: &str,
        transaction: &ConsumedAuthorizationTransaction,
    ) -> Result<VerifiedPortalIdentity, PortalOidcAuthenticationError> {
        if authorization_code.trim().is_empty()
            || transaction.nonce().is_empty()
            || transaction.code_verifier().is_empty()
        {
            return Err(PortalOidcAuthenticationError::InvalidRequest);
        }

        let redirect_uri = RedirectUrl::new(self.callback_uri.clone())
            .map_err(|_| PortalOidcAuthenticationError::ClientSetup)?;
        let client = CoreClient::from_provider_metadata(
            metadata,
            ClientId::new(self.client_id.clone()),
            self.client_secret.clone().map(ClientSecret::new),
        )
        .set_redirect_uri(redirect_uri);

        let http_client = reqwest::Client::builder()
            .redirect(reqwest::redirect::Policy::none())
            .timeout(TOKEN_REQUEST_TIMEOUT)
            .build()
            .map_err(|_| PortalOidcAuthenticationError::ClientSetup)?;

        let token_request = client
            .exchange_code(AuthorizationCode::new(authorization_code.to_owned()))
            .map_err(|_| PortalOidcAuthenticationError::ClientSetup)?
            .set_pkce_verifier(PkceCodeVerifier::new(
                transaction.code_verifier().to_owned(),
            ));

        let token_response = token_request
            .request_async(&http_client)
            .await
            .map_err(|_| PortalOidcAuthenticationError::CodeExchange)?;

        let id_token = token_response
            .extra_fields()
            .id_token()
            .ok_or(PortalOidcAuthenticationError::MissingIdToken)?;

        let claims = id_token
            .claims(
                &client.id_token_verifier(),
                &Nonce::new(transaction.nonce().to_owned()),
            )
            .map_err(|_| PortalOidcAuthenticationError::InvalidIdToken)?;

        let issuer = claims.issuer().as_str().to_owned();
        let subject = claims.subject().as_str().to_owned();
        if issuer.is_empty() || subject.is_empty() {
            return Err(PortalOidcAuthenticationError::InvalidIdentity);
        }

        Ok(VerifiedPortalIdentity { issuer, subject })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn verified_identity_debug_redacts_subject() {
        let identity = VerifiedPortalIdentity {
            issuer: "https://identity.example.test/".into(),
            subject: "provider-user-123".into(),
        };
        let rendered = format!("{identity:?}");
        assert!(rendered.contains("https://identity.example.test/"));
        assert!(!rendered.contains("provider-user-123"));
        assert!(rendered.contains("[REDACTED]"));
    }

    #[test]
    fn verified_identity_exposes_only_issuer_and_subject() {
        let identity = VerifiedPortalIdentity {
            issuer: "https://identity.example.test/".into(),
            subject: "stable-subject".into(),
        };
        assert_eq!(identity.issuer(), "https://identity.example.test/");
        assert_eq!(identity.subject(), "stable-subject");
    }
}
