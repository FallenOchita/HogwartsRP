use base64::{engine::general_purpose::URL_SAFE_NO_PAD, Engine as _};
use rand::RngCore;
use serde::{Deserialize, Serialize};
use std::{fs, path::{Path, PathBuf}, sync::Mutex};
use thiserror::Error;

#[derive(Debug, Error)]
pub enum PortalAccountStoreError {
    #[error("The account identity store could not be read or written.")]
    Storage,
    #[error("The account identity store contains invalid data.")]
    InvalidData,
}

#[derive(Clone, Serialize, Deserialize)]
struct StoredIdentity {
    issuer: String,
    subject: String,
    account_id: String,
}

/// Development JSON identity store. Identity keys are the verified OIDC
/// issuer + subject pair; email and display-name claims are never consulted.
/// A production deployment should replace this single-process adapter with a
/// transactional database and a unique constraint on (issuer, subject).
pub struct JsonPortalAccountStore {
    path: PathBuf,
    identities: Mutex<Vec<StoredIdentity>>,
}

impl JsonPortalAccountStore {
    pub fn open(path: impl AsRef<Path>) -> Result<Self, PortalAccountStoreError> {
        let path = path.as_ref().to_path_buf();
        let identities = if path.exists() {
            let bytes = fs::read(&path).map_err(|_| PortalAccountStoreError::Storage)?;
            serde_json::from_slice(&bytes).map_err(|_| PortalAccountStoreError::InvalidData)?
        } else { Vec::new() };
        Ok(Self { path, identities: Mutex::new(identities) })
    }

    pub fn resolve_or_create(&self, issuer: &str, subject: &str) -> Result<String, PortalAccountStoreError> {
        if issuer.trim().is_empty() || subject.trim().is_empty() {
            return Err(PortalAccountStoreError::InvalidData);
        }
        let mut identities = self.identities.lock().map_err(|_| PortalAccountStoreError::Storage)?;
        if let Some(existing) = identities.iter().find(|entry| entry.issuer == issuer && entry.subject == subject) {
            return Ok(existing.account_id.clone());
        }
        let mut random = [0u8; 32];
        rand::rngs::OsRng.fill_bytes(&mut random);
        let account_id = URL_SAFE_NO_PAD.encode(random);
        identities.push(StoredIdentity { issuer: issuer.to_owned(), subject: subject.to_owned(), account_id: account_id.clone() });
        if let Err(error) = self.persist(&identities) {
            identities.pop();
            return Err(error);
        }
        Ok(account_id)
    }

    fn persist(&self, identities: &[StoredIdentity]) -> Result<(), PortalAccountStoreError> {
        if let Some(parent) = self.path.parent() {
            fs::create_dir_all(parent).map_err(|_| PortalAccountStoreError::Storage)?;
        }
        let bytes = serde_json::to_vec_pretty(identities).map_err(|_| PortalAccountStoreError::Storage)?;
        let temporary = self.path.with_extension("json.tmp");
        fs::write(&temporary, bytes).map_err(|_| PortalAccountStoreError::Storage)?;
        if self.path.exists() {
            fs::remove_file(&self.path).map_err(|_| PortalAccountStoreError::Storage)?;
        }
        fs::rename(&temporary, &self.path).map_err(|_| PortalAccountStoreError::Storage)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::{SystemTime, UNIX_EPOCH};

    fn temp_path() -> PathBuf {
        std::env::temp_dir().join(format!("hogwartsrp-accounts-{}.json", SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_nanos()))
    }

    #[test]
    fn verified_issuer_subject_resolves_stably_across_restarts() {
        let path = temp_path();
        let first = JsonPortalAccountStore::open(&path).unwrap();
        let id = first.resolve_or_create("https://issuer.example/", "subject-1").unwrap();
        assert_eq!(first.resolve_or_create("https://issuer.example/", "subject-1").unwrap(), id);
        drop(first);
        let second = JsonPortalAccountStore::open(&path).unwrap();
        assert_eq!(second.resolve_or_create("https://issuer.example/", "subject-1").unwrap(), id);
        assert_ne!(second.resolve_or_create("https://other.example/", "subject-1").unwrap(), id);
        let _ = fs::remove_file(path);
    }

    #[test]
    fn corrupt_identity_file_fails_closed() {
        let path = temp_path();
        fs::write(&path, b"not-json").unwrap();
        assert!(matches!(JsonPortalAccountStore::open(&path), Err(PortalAccountStoreError::InvalidData)));
        let _ = fs::remove_file(path);
    }
}
