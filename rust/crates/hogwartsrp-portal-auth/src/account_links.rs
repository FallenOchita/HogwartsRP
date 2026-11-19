use serde::{Deserialize, Serialize};
use std::{
    fs,
    path::{Path, PathBuf},
    sync::Mutex,
};
use thiserror::Error;

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum AccountLinkProvider {
    Discord,
    Steam,
    Epic,
}

impl AccountLinkProvider {
    pub fn as_str(self) -> &'static str {
        match self {
            Self::Discord => "discord",
            Self::Steam => "steam",
            Self::Epic => "epic",
        }
    }
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AccountLinkStatus {
    pub provider: AccountLinkProvider,
    pub connected: bool,
}

#[derive(Debug, Error)]
pub enum AccountLinkStoreError {
    #[error("The account-link store could not be read or written.")]
    Storage,
    #[error("The account-link store contains invalid data.")]
    InvalidData,
    #[error("This external identity is already linked to another account.")]
    IdentityAlreadyOwned,
    #[error("This account already has an identity linked for this provider.")]
    ProviderAlreadyLinked,
}

#[derive(Clone, Serialize, Deserialize)]
struct StoredAccountLink {
    account_id: String,
    provider: AccountLinkProvider,
    external_subject: String,
}

/// Single-process development store for verified external identities.
///
/// Only trusted provider-verification code should call link_verified_identity.
/// The browser must never submit an external subject or account ID directly.
/// Production should replace this adapter with a transactional database and
/// unique constraints on (provider, external_subject) and (account_id, provider).
pub struct JsonAccountLinkStore {
    path: PathBuf,
    links: Mutex<Vec<StoredAccountLink>>,
}

impl JsonAccountLinkStore {
    pub fn open(path: impl AsRef<Path>) -> Result<Self, AccountLinkStoreError> {
        let path = path.as_ref().to_path_buf();
        let links = if path.exists() {
            let bytes = fs::read(&path).map_err(|_| AccountLinkStoreError::Storage)?;
            serde_json::from_slice(&bytes).map_err(|_| AccountLinkStoreError::InvalidData)?
        } else {
            Vec::new()
        };
        validate_links(&links)?;
        Ok(Self { path, links: Mutex::new(links) })
    }

    /// Store an identity only after the provider adapter has independently
    /// verified the OAuth/OpenID response. Never expose this as a raw HTTP input.
    pub fn link_verified_identity(
        &self,
        account_id: &str,
        provider: AccountLinkProvider,
        external_subject: &str,
    ) -> Result<(), AccountLinkStoreError> {
        if account_id.trim().is_empty() || external_subject.trim().is_empty() {
            return Err(AccountLinkStoreError::InvalidData);
        }
        let mut links = self.links.lock().map_err(|_| AccountLinkStoreError::Storage)?;
        if links.iter().any(|link| link.provider == provider && link.external_subject == external_subject) {
            return Err(AccountLinkStoreError::IdentityAlreadyOwned);
        }
        if links.iter().any(|link| link.account_id == account_id && link.provider == provider) {
            return Err(AccountLinkStoreError::ProviderAlreadyLinked);
        }
        links.push(StoredAccountLink {
            account_id: account_id.to_owned(),
            provider,
            external_subject: external_subject.to_owned(),
        });
        if let Err(error) = self.persist(&links) {
            links.pop();
            return Err(error);
        }
        Ok(())
    }

    /// Return provider connection flags only; external provider identifiers
    /// are never returned to the browser.
    pub fn statuses(&self, account_id: &str) -> Result<Vec<AccountLinkStatus>, AccountLinkStoreError> {
        let links = self.links.lock().map_err(|_| AccountLinkStoreError::Storage)?;
        Ok([
            AccountLinkProvider::Discord,
            AccountLinkProvider::Steam,
            AccountLinkProvider::Epic,
        ].into_iter().map(|provider| AccountLinkStatus {
            provider,
            connected: links.iter().any(|link| link.account_id == account_id && link.provider == provider),
        }).collect())
    }

    /// Unlink only the named provider from the authenticated account.
    pub fn unlink(
        &self,
        account_id: &str,
        provider: AccountLinkProvider,
    ) -> Result<bool, AccountLinkStoreError> {
        if account_id.trim().is_empty() {
            return Err(AccountLinkStoreError::InvalidData);
        }
        let mut links = self.links.lock().map_err(|_| AccountLinkStoreError::Storage)?;
        let Some(index) = links.iter().position(|link| link.account_id == account_id && link.provider == provider) else {
            return Ok(false);
        };
        let removed = links.remove(index);
        if let Err(error) = self.persist(&links) {
            links.insert(index, removed);
            return Err(error);
        }
        Ok(true)
    }

    fn persist(&self, links: &[StoredAccountLink]) -> Result<(), AccountLinkStoreError> {
        if let Some(parent) = self.path.parent() {
            fs::create_dir_all(parent).map_err(|_| AccountLinkStoreError::Storage)?;
        }
        let bytes = serde_json::to_vec_pretty(links).map_err(|_| AccountLinkStoreError::Storage)?;
        let temporary = self.path.with_extension("json.tmp");
        fs::write(&temporary, bytes).map_err(|_| AccountLinkStoreError::Storage)?;
        if self.path.exists() {
            fs::remove_file(&self.path).map_err(|_| AccountLinkStoreError::Storage)?;
        }
        fs::rename(&temporary, &self.path).map_err(|_| AccountLinkStoreError::Storage)
    }
}

fn validate_links(links: &[StoredAccountLink]) -> Result<(), AccountLinkStoreError> {
    for (index, link) in links.iter().enumerate() {
        if link.account_id.trim().is_empty() || link.external_subject.trim().is_empty()
            || links[..index].iter().any(|other| {
                (other.provider == link.provider && other.external_subject == link.external_subject)
                    || (other.account_id == link.account_id && other.provider == link.provider)
            })
        {
            return Err(AccountLinkStoreError::InvalidData);
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::{SystemTime, UNIX_EPOCH};

    fn path() -> PathBuf {
        std::env::temp_dir().join(format!(
            "hogwartsrp-account-links-{}.json",
            SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_nanos()
        ))
    }

    #[test]
    fn provider_links_are_unique_and_status_hides_external_subjects() {
        let path = path();
        let store = JsonAccountLinkStore::open(&path).unwrap();
        store.link_verified_identity("account-a", AccountLinkProvider::Discord, "discord-user-1").unwrap();
        assert_eq!(
            store.link_verified_identity("account-b", AccountLinkProvider::Discord, "discord-user-1").unwrap_err().to_string(),
            AccountLinkStoreError::IdentityAlreadyOwned.to_string()
        );
        assert_eq!(
            store.link_verified_identity("account-a", AccountLinkProvider::Discord, "discord-user-2").unwrap_err().to_string(),
            AccountLinkStoreError::ProviderAlreadyLinked.to_string()
        );
        let statuses = store.statuses("account-a").unwrap();
        assert_eq!(statuses.len(), 3);
        assert!(statuses.iter().find(|item| item.provider == AccountLinkProvider::Discord).unwrap().connected);
        assert!(!statuses.iter().find(|item| item.provider == AccountLinkProvider::Steam).unwrap().connected);
        drop(store);
        let _ = fs::remove_file(path);
    }

    #[test]
    fn unlink_is_scoped_to_the_current_account_and_provider() {
        let path = path();
        let store = JsonAccountLinkStore::open(&path).unwrap();
        store.link_verified_identity("account-a", AccountLinkProvider::Steam, "steam-1").unwrap();
        assert!(!store.unlink("account-b", AccountLinkProvider::Steam).unwrap());
        assert!(store.unlink("account-a", AccountLinkProvider::Steam).unwrap());
        assert!(!store.statuses("account-a").unwrap().iter().find(|item| item.provider == AccountLinkProvider::Steam).unwrap().connected);
        drop(store);
        let _ = fs::remove_file(path);
    }

    #[test]
    fn links_survive_reopening_and_corrupt_files_fail_closed() {
        let path = path();
        {
            let store = JsonAccountLinkStore::open(&path).unwrap();
            store.link_verified_identity("account-a", AccountLinkProvider::Epic, "epic-1").unwrap();
        }
        let reopened = JsonAccountLinkStore::open(&path).unwrap();
        assert!(reopened.statuses("account-a").unwrap().iter().find(|item| item.provider == AccountLinkProvider::Epic).unwrap().connected);
        drop(reopened);
        fs::write(&path, b"not-json").unwrap();
        assert!(matches!(JsonAccountLinkStore::open(&path), Err(AccountLinkStoreError::InvalidData)));
        let _ = fs::remove_file(path);
    }
}
