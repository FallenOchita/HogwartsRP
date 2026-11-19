//! Rust equivalents of the former C# launcher service layer.
//! These routines are client-side conveniences only; the server remains authoritative.

use regex::Regex;
use reqwest::blocking::Client;
use serde::{Deserialize, Serialize};
use std::{
    fs,
    path::{Path, PathBuf},
    process::Command,
    time::Duration,
};
use tempfile::NamedTempFile;
use url::Url;

#[derive(Clone, Debug, Serialize, Deserialize, PartialEq, Eq)]
pub struct ServerProfile {
    pub id: String,
    pub name: String,
    pub address: String,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct WandData {
    pub wood: Option<String>,
    pub core: Option<String>,
    pub length: Option<f64>,
    pub flexibility: Option<String>,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RoleplayProfile {
    pub background: Option<String>,
    pub biography: Option<String>,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CharacterSubmission {
    pub name: String,
    pub appearance_json: Option<String>,
    pub house: String,
    pub wand: Option<WandData>,
    pub background: Option<String>,
    pub roleplay_profile: Option<RoleplayProfile>,
}

#[derive(Clone, Debug, Default)]
pub struct CharacterCreationDraft {
    pub name: String,
    pub house: String,
    pub background: Option<String>,
    pub biography: Option<String>,
    pub wand_wood: Option<String>,
    pub wand_core: Option<String>,
    pub wand_length: Option<f64>,
    pub wand_flexibility: Option<String>,
}

impl CharacterCreationDraft {
    pub fn try_build_submission(&self) -> Result<CharacterSubmission, Vec<String>> {
        let mut errors = Vec::new();
        let name = self.name.trim();
        let name_pattern = Regex::new(r"^[\p{L}\p{M} '’-]+$").expect("static name regex");
        if name.chars().count() < 1 || name.chars().count() > 32 || !name_pattern.is_match(name) {
            errors.push("Name must be 1–32 letters, spaces, apostrophes, or hyphens.".to_owned());
        }

        let house = ["Gryffindor", "Hufflepuff", "Ravenclaw", "Slytherin"]
            .iter()
            .find(|candidate| candidate.eq_ignore_ascii_case(self.house.trim()))
            .map(|value| (*value).to_owned());
        if house.is_none() {
            errors.push("Choose Gryffindor, Hufflepuff, Ravenclaw, or Slytherin.".to_owned());
        }

        let background = clean_optional(self.background.as_deref());
        let biography = clean_optional(self.biography.as_deref());
        if background.as_ref().is_some_and(|value| value.chars().count() > 2_000) {
            errors.push("Background must be 2,000 characters or fewer.".to_owned());
        }
        if biography.as_ref().is_some_and(|value| value.chars().count() > 4_000) {
            errors.push("Biography must be 4,000 characters or fewer.".to_owned());
        }
        if self.wand_length.is_some_and(|value| !(4.0..=16.0).contains(&value)) {
            errors.push("Wand length must be between 4 and 16 inches.".to_owned());
        }
        if !errors.is_empty() {
            return Err(errors);
        }

        let wand_wood = clean_optional(self.wand_wood.as_deref());
        let wand_core = clean_optional(self.wand_core.as_deref());
        let wand_flexibility = clean_optional(self.wand_flexibility.as_deref());
        let has_wand = wand_wood.is_some() || wand_core.is_some()
            || self.wand_length.is_some() || wand_flexibility.is_some();
        let wand = has_wand.then_some(WandData {
            wood: wand_wood,
            core: wand_core,
            length: self.wand_length,
            flexibility: wand_flexibility,
        });
        let roleplay_profile = (background.is_some() || biography.is_some()).then_some(RoleplayProfile {
            background: background.clone(),
            biography,
        });

        Ok(CharacterSubmission {
            name: name.to_owned(),
            appearance_json: None,
            house: house.expect("house validated above"),
            wand,
            background,
            roleplay_profile,
        })
    }
}

fn clean_optional(value: Option<&str>) -> Option<String> {
    value.map(str::trim).filter(|value| !value.is_empty()).map(str::to_owned)
}

pub struct JsonServerProfileStore {
    path: PathBuf,
}

impl JsonServerProfileStore {
    pub fn new(path: impl Into<PathBuf>) -> Self {
        Self { path: path.into() }
    }

    pub fn load(&self) -> Result<Vec<ServerProfile>, String> {
        if !self.path.exists() {
            return Ok(Vec::new());
        }
        let bytes = fs::read(&self.path).map_err(|error| error.to_string())?;
        let mut profiles: Vec<ServerProfile> =
            serde_json::from_slice(&bytes).map_err(|_| "Saved server profiles contain invalid JSON.".to_owned())?;
        validate_profiles(&profiles)?;
        profiles.sort_by_key(|profile| profile.name.to_lowercase());
        Ok(profiles)
    }

    pub fn save(&self, name: &str, address: &str) -> Result<ServerProfile, String> {
        let name = name.trim();
        if name.is_empty() || name.chars().count() > 64 {
            return Err("Server names must contain between 1 and 64 characters.".into());
        }
        validate_http_address(address)?;
        let mut profiles = self.load()?;
        let existing = profiles.iter().position(|profile| {
            profile.address.trim_end_matches('/').eq_ignore_ascii_case(address.trim_end_matches('/'))
        });
        let id = existing.map(|index| profiles.remove(index).id)
            .unwrap_or_else(|| format!("{:032x}", randless_id()));
        let saved = ServerProfile { id, name: name.to_owned(), address: address.trim().to_owned() };
        profiles.push(saved.clone());
        self.write_atomically(&profiles)?;
        Ok(saved)
    }

    pub fn delete(&self, id: &str) -> Result<bool, String> {
        if id.trim().is_empty() { return Ok(false); }
        let mut profiles = self.load()?;
        let before = profiles.len();
        profiles.retain(|profile| profile.id != id);
        if profiles.len() == before { return Ok(false); }
        self.write_atomically(&profiles)?;
        Ok(true)
    }

    fn write_atomically(&self, profiles: &[ServerProfile]) -> Result<(), String> {
        let parent = self.path.parent().ok_or_else(|| "Invalid profile file path.".to_owned())?;
        fs::create_dir_all(parent).map_err(|error| error.to_string())?;
        let mut temporary = NamedTempFile::new_in(parent).map_err(|error| error.to_string())?;
        serde_json::to_writer_pretty(&mut temporary, profiles).map_err(|error| error.to_string())?;
        temporary.persist(&self.path).map(|_| ()).map_err(|error| error.error.to_string())
    }
}

fn randless_id() -> u128 {
    use std::sync::atomic::{AtomicU64, Ordering};
    static NEXT: AtomicU64 = AtomicU64::new(1);
    ((std::process::id() as u128) << 64) | NEXT.fetch_add(1, Ordering::Relaxed) as u128
}

fn validate_profiles(profiles: &[ServerProfile]) -> Result<(), String> {
    let mut ids = std::collections::HashSet::new();
    for profile in profiles {
        if profile.id.trim().is_empty() || profile.name.trim().is_empty()
            || profile.name.chars().count() > 64 || validate_http_address(&profile.address).is_err()
            || !ids.insert(profile.id.as_str()) {
            return Err("Saved server profiles contain an invalid or duplicate profile.".into());
        }
    }
    Ok(())
}

pub fn validate_server_endpoint(address: &str) -> Result<Url, String> {
    let url = Url::parse(address).map_err(|_| "Server address must be an absolute URL.".to_owned())?;
    let local = url.host_str().is_some_and(|host| host.eq_ignore_ascii_case("localhost")
        || host == "127.0.0.1" || host == "::1");
    match url.scheme() {
        "https" | "wss" => {}
        "http" | "ws" if local => {}
        _ => return Err("Server endpoint must use HTTPS/WSS, or HTTP/WS on loopback for development.".into()),
    }
    if url.query().is_some() || url.fragment().is_some() || !matches!(url.path(), "/" | "/connect") {
        return Err("Server endpoint must be the server root or /connect without a query or fragment.".into());
    }
    Ok(url)
}

fn validate_http_address(address: &str) -> Result<Url, String> {
    let url = Url::parse(address).map_err(|_| "Server address must be an absolute HTTP or HTTPS URL.".to_owned())?;
    if !matches!(url.scheme(), "http" | "https") || url.host_str().is_none() {
        return Err("Server address must be an absolute HTTP or HTTPS URL.".into());
    }
    Ok(url)
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ServerStatusSnapshot {
    pub status: String,
    pub server_name: String,
    pub online_players: u32,
    pub max_players: u32,
    pub checked_at_utc: String,
}

pub struct ServerStatusClient {
    client: Client,
}

impl Default for ServerStatusClient {
    fn default() -> Self {
        Self {
            client: Client::builder().timeout(Duration::from_secs(5)).build()
                .expect("valid default HTTP client"),
        }
    }
}

impl ServerStatusClient {
    pub fn get_status(&self, address: &str) -> Result<ServerStatusSnapshot, String> {
        let mut url = validate_http_address(address)?;
        url.set_path("/status");
        url.set_query(None);
        url.set_fragment(None);
        let response = self.client.get(url).send().map_err(|error| error.to_string())?
            .error_for_status().map_err(|error| error.to_string())?;
        let status: ServerStatusSnapshot = response.json().map_err(|error| error.to_string())?;
        if !status.status.eq_ignore_ascii_case("online") || status.server_name.trim().is_empty()
            || status.server_name.chars().count() > 64 || status.online_players > status.max_players
            || status.max_players == 0 || status.checked_at_utc.trim().is_empty() {
            return Err("Server returned an invalid status response.".into());
        }
        Ok(status)
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub enum GamePlatform { Steam, Epic, Unknown }

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct GameInstallation {
    pub executable_path: PathBuf,
    pub install_directory: PathBuf,
    pub platform: GamePlatform,
}

pub struct GameInstallationDetector {
    additional_roots: Vec<PathBuf>,
}

impl GameInstallationDetector {
    pub fn new(additional_roots: Vec<PathBuf>) -> Self { Self { additional_roots } }

    pub fn detect(&self) -> Option<GameInstallation> {
        let mut roots = self.additional_roots.clone();
        if cfg!(target_os = "windows") {
            if let Some(program_files) = std::env::var_os("PROGRAMFILES(X86)") {
                roots.push(PathBuf::from(program_files).join("Steam/steamapps/common/Hogwarts Legacy"));
            }
            if let Some(program_files) = std::env::var_os("PROGRAMFILES") {
                roots.push(PathBuf::from(program_files).join("Epic Games/HogwartsLegacy"));
            }
        } else if cfg!(target_os = "linux") {
            if let Some(home) = std::env::var_os("HOME") {
                let home = PathBuf::from(home);
                roots.push(home.join(".steam/steam/steamapps/common/Hogwarts Legacy"));
                roots.push(home.join(".local/share/Steam/steamapps/common/Hogwarts Legacy"));
            }
        }
        roots.into_iter().find_map(|root| {
            let executable = root.join("Phoenix/Binaries/Win64/HogwartsLegacy.exe");
            if !executable.is_file() { return None; }
            let root_text = root.to_string_lossy().to_lowercase();
            let platform = if root_text.contains("steam") { GamePlatform::Steam }
                else if root_text.contains("epic") { GamePlatform::Epic }
                else { GamePlatform::Unknown };
            (platform != GamePlatform::Unknown).then_some(GameInstallation {
                executable_path: executable,
                install_directory: root,
                platform,
            })
        })
    }
}

pub fn launch_steam(installation: &GameInstallation) -> Result<(), String> {
    if installation.platform != GamePlatform::Steam {
        return Err("This platform adapter supports Steam installations only.".into());
    }
    let root = installation.install_directory.canonicalize().map_err(|error| error.to_string())?;
    let executable = installation.executable_path.canonicalize().map_err(|error| error.to_string())?;
    if !executable.starts_with(&root) || !executable.is_file() {
        return Err("The selected game executable is missing or outside its installation directory.".into());
    }
    #[cfg(target_os = "windows")]
    let result = Command::new("cmd").args(["/C", "start", "", "steam://run/990080"]).spawn();
    #[cfg(target_os = "linux")]
    let result = Command::new("xdg-open").arg("steam://run/990080").spawn();
    #[cfg(target_os = "macos")]
    let result = Command::new("open").arg("steam://run/990080").spawn();
    result.map(|_| ()).map_err(|error| error.to_string())
}

#[allow(dead_code)]
fn _path_is_within(root: &Path, candidate: &Path) -> bool {
    candidate.starts_with(root)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn character_draft_maps_house_and_optional_wand() {
        let draft = CharacterCreationDraft {
            name: "  Élodie Ashbourne ".into(),
            house: "ravenclaw".into(),
            wand_wood: Some("Oak".into()),
            wand_core: Some("Phoenix feather".into()),
            wand_length: Some(11.0),
            ..Default::default()
        };
        let result = draft.try_build_submission().unwrap();
        assert_eq!(result.name, "Élodie Ashbourne");
        assert_eq!(result.house, "Ravenclaw");
        assert_eq!(result.wand.unwrap().wood.as_deref(), Some("Oak"));
        assert!(result.appearance_json.is_none());
    }

    #[test]
    fn character_draft_rejects_invalid_name_house_and_wand() {
        let draft = CharacterCreationDraft {
            name: "Bad_Name".into(),
            house: "Ilvermorny".into(),
            wand_length: Some(30.0),
            ..Default::default()
        };
        assert_eq!(draft.try_build_submission().unwrap_err().len(), 3);
    }

    #[test]
    fn endpoint_validation_requires_secure_remote_transport() {
        assert!(validate_server_endpoint("https://example.test").is_ok());
        assert!(validate_server_endpoint("http://127.0.0.1:7777").is_ok());
        assert!(validate_server_endpoint("http://example.test").is_err());
        assert!(validate_server_endpoint("ws://example.test").is_err());
    }

    #[test]
    fn profile_store_saves_updates_and_deletes_atomically() {
        let directory = tempfile::tempdir().unwrap();
        let store = JsonServerProfileStore::new(directory.path().join("profiles.json"));
        let first = store.save(" Test Server ", "https://example.test").unwrap();
        let updated = store.save("Renamed", "https://example.test/").unwrap();
        assert_eq!(first.id, updated.id);
        assert_eq!(store.load().unwrap()[0].name, "Renamed");
        assert!(store.delete(&updated.id).unwrap());
        assert!(store.load().unwrap().is_empty());
    }
}
