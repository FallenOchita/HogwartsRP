use base64::{engine::general_purpose::URL_SAFE_NO_PAD, Engine as _};
use rand::RngCore;
use sha2::{Digest, Sha256};
use std::{collections::HashMap, sync::Mutex};
use subtle::ConstantTimeEq;
use thiserror::Error;

const SESSION_TTL_SECONDS: u64 = 12 * 60 * 60;
const DEFAULT_SESSION_CAPACITY: usize = 100_000;

#[derive(Debug, Error, PartialEq, Eq)]
pub enum BrowserSessionError {
    #[error("The browser session store is unavailable.")]
    Unavailable,
    #[error("The browser session store is full.")]
    CapacityReached,
    #[error("The browser session is invalid or expired.")]
    InvalidOrExpired,
    #[error("The CSRF token is invalid.")]
    InvalidCsrf,
}

struct StoredSession {
    account_id: String,
    csrf_hash: [u8; 32],
    expires_at: u64,
}

/// In-memory browser sessions. Only token digests are retained; the raw bearer
/// and CSRF tokens are returned once to the trusted HTTP layer.
pub struct BrowserSessionStore {
    sessions: Mutex<HashMap<[u8; 32], StoredSession>>,
    capacity: usize,
}

pub struct IssuedBrowserSession {
    pub session_token: String,
    pub csrf_token: String,
    pub expires_at: u64,
}

impl Default for BrowserSessionStore {
    fn default() -> Self { Self::new(DEFAULT_SESSION_CAPACITY) }
}

impl BrowserSessionStore {
    pub fn new(capacity: usize) -> Self {
        Self { sessions: Mutex::new(HashMap::new()), capacity: capacity.max(1) }
    }

    pub fn issue(&self, account_id: &str, now: u64) -> Result<IssuedBrowserSession, BrowserSessionError> {
        if account_id.trim().is_empty() { return Err(BrowserSessionError::InvalidOrExpired); }
        let mut token = [0u8; 32];
        let mut csrf = [0u8; 32];
        let mut rng = rand::rngs::OsRng;
        rng.fill_bytes(&mut token);
        rng.fill_bytes(&mut csrf);
        let session_token = URL_SAFE_NO_PAD.encode(token);
        let csrf_token = URL_SAFE_NO_PAD.encode(csrf);
        let token_hash = digest(&session_token);
        let csrf_hash = digest(&csrf_token);
        let expires_at = now.saturating_add(SESSION_TTL_SECONDS);
        let mut sessions = self.sessions.lock().map_err(|_| BrowserSessionError::Unavailable)?;
        sessions.retain(|_, session| session.expires_at > now);
        if sessions.len() >= self.capacity { return Err(BrowserSessionError::CapacityReached); }
        sessions.insert(token_hash, StoredSession { account_id: account_id.to_owned(), csrf_hash, expires_at });
        Ok(IssuedBrowserSession { session_token, csrf_token, expires_at })
    }

    pub fn account_for(&self, token: &str, now: u64) -> Result<(String, u64), BrowserSessionError> {
        let hash = digest(token);
        let mut sessions = self.sessions.lock().map_err(|_| BrowserSessionError::Unavailable)?;
        let Some(session) = sessions.get(&hash) else { return Err(BrowserSessionError::InvalidOrExpired); };
        if session.expires_at <= now {
            sessions.remove(&hash);
            return Err(BrowserSessionError::InvalidOrExpired);
        }
        Ok((session.account_id.clone(), session.expires_at))
    }

    pub fn validate_csrf(&self, token: &str, csrf: &str, now: u64) -> Result<(), BrowserSessionError> {
        let hash = digest(token);
        let supplied = digest(csrf);
        let sessions = self.sessions.lock().map_err(|_| BrowserSessionError::Unavailable)?;
        let Some(session) = sessions.get(&hash) else { return Err(BrowserSessionError::InvalidOrExpired); };
        if session.expires_at <= now { return Err(BrowserSessionError::InvalidOrExpired); }
        if !bool::from(session.csrf_hash.ct_eq(&supplied)) { return Err(BrowserSessionError::InvalidCsrf); }
        Ok(())
    }

    pub fn revoke(&self, token: &str) -> Result<(), BrowserSessionError> {
        let hash = digest(token);
        let mut sessions = self.sessions.lock().map_err(|_| BrowserSessionError::Unavailable)?;
        sessions.remove(&hash);
        Ok(())
    }

    pub fn len(&self) -> Result<usize, BrowserSessionError> {
        self.sessions.lock().map(|sessions| sessions.len()).map_err(|_| BrowserSessionError::Unavailable)
    }
}

fn digest(value: &str) -> [u8; 32] { Sha256::digest(value.as_bytes()).into() }

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn session_binds_account_and_csrf_and_revokes_independently() {
        let store = BrowserSessionStore::new(4);
        let issued = store.issue("account-a", 100).unwrap();
        let (account, expiry) = store.account_for(&issued.session_token, 101).unwrap();
        assert_eq!(account, "account-a");
        assert_eq!(expiry, issued.expires_at);
        assert!(store.validate_csrf(&issued.session_token, &issued.csrf_token, 101).is_ok());
        assert_eq!(store.validate_csrf(&issued.session_token, "wrong", 101), Err(BrowserSessionError::InvalidCsrf));
        store.revoke(&issued.session_token).unwrap();
        assert_eq!(store.account_for(&issued.session_token, 101), Err(BrowserSessionError::InvalidOrExpired));
    }

    #[test]
    fn expired_sessions_are_rejected_and_pruned_on_issue() {
        let store = BrowserSessionStore::new(1);
        let first = store.issue("account-a", 100).unwrap();
        assert_eq!(store.account_for(&first.session_token, first.expires_at), Err(BrowserSessionError::InvalidOrExpired));
        assert!(store.issue("account-b", first.expires_at).is_ok());
    }

    #[test]
    fn session_capacity_is_bounded() {
        let store = BrowserSessionStore::new(1);
        let _first = store.issue("account-a", 100).unwrap();
        assert_eq!(store.issue("account-b", 100).unwrap_err(), BrowserSessionError::CapacityReached);
    }
}
