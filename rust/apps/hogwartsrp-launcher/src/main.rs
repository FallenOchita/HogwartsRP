mod directory;

use directories::ProjectDirs;
use eframe::egui;
use serde::{Deserialize, Serialize};
use std::{
    fs,
    io::Write,
    path::PathBuf,
    sync::mpsc::{self, Receiver},
    thread,
    time::Duration,
};
use url::Url;

use directory::{DirectoryPage, fetch_servers};

#[derive(Clone, Debug, Serialize, Deserialize)]
struct ServerProfile {
    name: String,
    address: String,
}

#[derive(Deserialize)]
struct ServerStatus {
    status: String,
    #[serde(rename = "serverName")]
    server_name: String,
    #[serde(rename = "onlinePlayers")]
    online_players: u32,
    #[serde(rename = "maxPlayers")]
    max_players: u32,
}

struct HogwartsRpLauncher {
    server_name: String,
    server_address: String,
    saved_servers: Vec<ServerProfile>,
    selected_server: Option<usize>,
    status_message: String,
    server_status: String,
    status_receiver: Option<Receiver<Result<String, String>>>,
    account_portal: String,
    directory_url: String,
    directory_search: String,
    directory_page: Option<DirectoryPage>,
    directory_status: String,
    directory_receiver: Option<Receiver<Result<DirectoryPage, String>>>,
    directory_page_number: u32,
}

impl Default for HogwartsRpLauncher {
    fn default() -> Self {
        let saved_servers = load_profiles();
        let account_portal = std::env::var("HOGWARTSRP_ACCOUNT_PORTAL_URL").unwrap_or_default();
        let directory_url = std::env::var("HOGWARTSRP_DIRECTORY_URL")
            .unwrap_or_else(|_| "http://127.0.0.1:8790".into());
        Self {
            server_name: "Local development".into(),
            server_address: "http://127.0.0.1:7777".into(),
            saved_servers,
            selected_server: None,
            status_message: "Choose a server to begin.".into(),
            server_status: "Not checked".into(),
            status_receiver: None,
            account_portal,
            directory_url,
            directory_search: String::new(),
            directory_page: None,
            directory_status: "Not loaded. Configure the directory URL and select Refresh.".into(),
            directory_receiver: None,
            directory_page_number: 1,
        }
    }
}

impl HogwartsRpLauncher {
    fn save_profiles(&self) -> Result<(), String> {
        let path = profile_path()?;
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent).map_err(|e| e.to_string())?;
        }
        let json = serde_json::to_vec_pretty(&self.saved_servers).map_err(|e| e.to_string())?;
        let parent = path.parent().ok_or_else(|| "Invalid profile path.".to_owned())?;
        let mut temporary = tempfile::NamedTempFile::new_in(parent).map_err(|e| e.to_string())?;
        temporary.write_all(&json).map_err(|e| e.to_string())?;
        temporary.persist(&path).map(|_| ()).map_err(|e| e.error.to_string())
    }

    fn save_current_server(&mut self) {
        let name = self.server_name.trim();
        let address = self.server_address.trim();
        if name.is_empty() || name.len() > 64 {
            self.status_message = "Server name must contain 1–64 characters.".into();
            return;
        }
        let Ok(url) = Url::parse(address) else {
            self.status_message = "Enter a valid absolute HTTP or HTTPS server address.".into();
            return;
        };
        if !matches!(url.scheme(), "http" | "https") || url.host_str().is_none() {
            self.status_message = "Server address must use HTTP or HTTPS.".into();
            return;
        }
        if let Some(existing) = self.saved_servers.iter_mut().find(|p| {
            p.address.trim_end_matches('/').eq_ignore_ascii_case(address.trim_end_matches('/'))
        }) {
            existing.name = name.to_owned();
            existing.address = address.to_owned();
        } else {
            self.saved_servers.push(ServerProfile { name: name.to_owned(), address: address.to_owned() });
        }
        match self.save_profiles() {
            Ok(()) => self.status_message = format!("Saved server profile: {name}"),
            Err(error) => self.status_message = format!("Could not save server profile: {error}"),
        }
    }

    fn check_server_status(&mut self) {
        let address = self.server_address.trim().to_owned();
        let (sender, receiver) = mpsc::channel();
        self.status_receiver = Some(receiver);
        self.server_status = "Checking…".into();
        thread::spawn(move || {
            let result = (|| {
                let mut url = Url::parse(&address).map_err(|e| e.to_string())?;
                if !matches!(url.scheme(), "http" | "https") {
                    return Err("Status endpoint requires HTTP or HTTPS.".into());
                }
                url.set_path("/status");
                url.set_query(None);
                url.set_fragment(None);
                let client = reqwest::blocking::Client::builder()
                    .timeout(Duration::from_secs(5))
                    .build()
                    .map_err(|e| e.to_string())?;
                let response = client.get(url).send().map_err(|e| e.to_string())?
                    .error_for_status().map_err(|e| e.to_string())?;
                let status: ServerStatus = response.json().map_err(|e| e.to_string())?;
                if status.status != "online" || status.server_name.trim().is_empty()
                    || status.online_players > status.max_players || status.max_players == 0 {
                    return Err("Server returned an invalid status response.".into());
                }
                Ok(format!("{} · {}/{} players", status.server_name, status.online_players, status.max_players))
            })();
            let _ = sender.send(result);
        });
    }

    fn refresh_directory(&mut self) {
        let directory_url = self.directory_url.trim().to_owned();
        let search = self.directory_search.trim().to_owned();
        let page = self.directory_page_number;
        let (sender, receiver) = mpsc::channel();
        self.directory_receiver = Some(receiver);
        self.directory_status = "Loading public servers…".into();
        thread::spawn(move || {
            let _ = sender.send(fetch_servers(&directory_url, &search, page));
        });
    }

    fn poll_directory(&mut self, ctx: &egui::Context) {
        let Some(receiver) = &self.directory_receiver else { return; };
        match receiver.try_recv() {
            Ok(Ok(page)) => {
                self.directory_status = format!("Loaded {} of {} public server(s).", page.servers.len(), page.total);
                self.directory_page = Some(page);
                self.directory_receiver = None;
            }
            Ok(Err(error)) => {
                self.directory_status = format!("Could not load public servers: {error}");
                self.directory_receiver = None;
            }
            Err(mpsc::TryRecvError::Disconnected) => {
                self.directory_status = "Directory request stopped unexpectedly.".into();
                self.directory_receiver = None;
            }
            Err(mpsc::TryRecvError::Empty) => ctx.request_repaint_after(Duration::from_millis(100)),
        }
    }

    fn open_account_portal(&mut self) {
        if self.account_portal.trim().is_empty() {
            self.status_message = "Account portal URL is not configured yet.".into();
            return;
        }
        match Url::parse(&self.account_portal) {
            Ok(url) if matches!(url.scheme(), "https" | "http") => match open::that(url.as_str()) {
                Ok(()) => self.status_message = "Opened the HogwartsRP account portal in your browser.".into(),
                Err(error) => self.status_message = format!("Could not open account portal: {error}"),
            },
            _ => self.status_message = "Account portal configuration must be an absolute HTTP or HTTPS URL.".into(),
        }
    }

    fn launch_game(&mut self) {
        match detect_steam_installation() {
            Some(_) => match open::that("steam://run/990080") {
                Ok(()) => self.status_message = "Steam launch request sent. Game startup is not yet confirmed.".into(),
                Err(error) => self.status_message = format!("Could not open Steam: {error}"),
            },
            None => self.status_message = "Steam Hogwarts Legacy installation was not found in the standard library paths. Custom Steam library discovery is not implemented yet.".into(),
        }
    }

    fn poll_status(&mut self, ctx: &egui::Context) {
        let Some(receiver) = &self.status_receiver else { return; };
        match receiver.try_recv() {
            Ok(Ok(status)) => {
                self.server_status = status;
                self.status_message = "Server status check completed.".into();
                self.status_receiver = None;
            }
            Ok(Err(error)) => {
                self.server_status = "Unavailable".into();
                self.status_message = format!("Server status check failed: {error}");
                self.status_receiver = None;
            }
            Err(mpsc::TryRecvError::Disconnected) => {
                self.server_status = "Unavailable".into();
                self.status_message = "Server status worker stopped unexpectedly.".into();
                self.status_receiver = None;
            }
            Err(mpsc::TryRecvError::Empty) => ctx.request_repaint_after(Duration::from_millis(100)),
        }
    }
}

impl eframe::App for HogwartsRpLauncher {
    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        self.poll_status(ctx);
        self.poll_directory(ctx);
        egui::CentralPanel::default().show(ctx, |ui| {
            ui.heading("HOGWARTSRP");
            ui.label("Native Rust launcher · Hogwarts Legacy roleplay framework");
            ui.add_space(14.0);

            ui.group(|ui| {
                ui.heading("Public servers");
                ui.horizontal(|ui| {
                    ui.label("Directory");
                    ui.text_edit_singleline(&mut self.directory_url);
                });
                ui.horizontal(|ui| {
                    ui.label("Search");
                    ui.text_edit_singleline(&mut self.directory_search);
                    if ui.button("Refresh").clicked() {
                        self.directory_page_number = 1;
                        self.refresh_directory();
                    }
                });
                ui.label(&self.directory_status);

                let mut chosen_server: Option<(String, String)> = None;
                if let Some(page) = self.directory_page.clone() {
                    egui::ScrollArea::vertical().max_height(220.0).show(ui, |ui| {
                        for server in &page.servers {
                            ui.group(|ui| {
                                ui.horizontal(|ui| {
                                    ui.strong(&server.name);
                                    ui.label(format!("{} · {}/{} players", server.status, server.online_players, server.max_players));
                                    if ui.button("Select").clicked() {
                                        chosen_server = Some((server.name.clone(), server.endpoint.clone()));
                                    }
                                });
                                if let Some(description) = &server.description {
                                    ui.label(description);
                                }
                            });
                        }
                        if page.servers.is_empty() {
                            ui.label("No public servers matched this search.");
                        }
                    });
                    let total_pages = ((page.total + u64::from(page.page_size) - 1) / u64::from(page.page_size)).max(1) as u32;
                    ui.horizontal(|ui| {
                        if ui.add_enabled(page.page > 1, egui::Button::new("Previous")).clicked() {
                            self.directory_page_number = page.page - 1;
                            self.refresh_directory();
                        }
                        ui.label(format!("Page {} of {}", page.page, total_pages));
                        if ui.add_enabled(page.page < total_pages, egui::Button::new("Next")).clicked() {
                            self.directory_page_number = page.page + 1;
                            self.refresh_directory();
                        }
                    });
                } else {
                    ui.label("The public directory has not been loaded.");
                }
                if let Some((name, endpoint)) = chosen_server {
                    self.server_name = name;
                    self.server_address = endpoint;
                    self.server_status = "Not checked".into();
                    self.status_message = "Selected a public listing. Check status or save it to your server profiles.".into();
                }
            });

            ui.add_space(10.0);
            ui.group(|ui| {
                ui.heading("Server connection profile");
                if !self.saved_servers.is_empty() {
                    let selected_text = self.selected_server
                        .and_then(|i| self.saved_servers.get(i))
                        .map(|p| p.name.as_str())
                        .unwrap_or("Choose saved server…");
                    egui::ComboBox::from_label("Saved servers")
                        .selected_text(selected_text)
                        .show_ui(ui, |ui| {
                            for (index, profile) in self.saved_servers.iter().enumerate() {
                                if ui.selectable_label(self.selected_server == Some(index), &profile.name).clicked() {
                                    self.selected_server = Some(index);
                                    self.server_name = profile.name.clone();
                                    self.server_address = profile.address.clone();
                                }
                            }
                        });
                }
                ui.horizontal(|ui| {
                    ui.label("Name");
                    ui.text_edit_singleline(&mut self.server_name);
                });
                ui.horizontal(|ui| {
                    ui.label("Address");
                    ui.text_edit_singleline(&mut self.server_address);
                });
                ui.horizontal(|ui| {
                    if ui.button("Save server").clicked() { self.save_current_server(); }
                    if ui.button("Remove selected").clicked() {
                        if let Some(index) = self.selected_server.take() {
                            if index < self.saved_servers.len() {
                                self.saved_servers.remove(index);
                                match self.save_profiles() {
                                    Ok(()) => self.status_message = "Removed saved server.".into(),
                                    Err(error) => self.status_message = format!("Could not update saved servers: {error}"),
                                }
                            }
                        }
                    }
                    if ui.button("Check status").clicked() { self.check_server_status(); }
                });
                ui.label(format!("Status: {}", self.server_status));
            });

            ui.add_space(10.0);
            ui.group(|ui| {
                ui.heading("Account");
                if ui.button("Open account portal").clicked() { self.open_account_portal(); }
                ui.label("Discord, Steam, and Epic account linking is handled by the central browser portal when configured.");
            });

            ui.add_space(10.0);
            ui.group(|ui| {
                ui.heading("Game");
                if ui.button("Play Hogwarts Legacy through Steam").clicked() { self.launch_game(); }
                ui.label("Steam performs its normal ownership and sign-in checks.");
            });

            ui.add_space(12.0);
            ui.separator();
            ui.label(&self.status_message);
            ui.add_space(8.0);
            ui.small("This Rust launcher currently supports public directory browsing, saved server profiles, read-only status checks, account-portal opening, and Steam launch requests. Authenticated server connection, character submission, auto-update, and in-game multiplayer are not yet implemented in this executable.");
        });
    }
}

fn profile_path() -> Result<PathBuf, String> {
    let dirs = ProjectDirs::from("com", "FallenOchita", "HogwartsRP")
        .ok_or_else(|| "Could not determine the application config directory.".to_owned())?;
    Ok(dirs.config_dir().join("server-profiles.json"))
}

fn load_profiles() -> Vec<ServerProfile> {
    let Ok(path) = profile_path() else { return Vec::new(); };
    let Ok(data) = fs::read(path) else { return Vec::new(); };
    serde_json::from_slice::<Vec<ServerProfile>>(&data).unwrap_or_default()
}

fn detect_steam_installation() -> Option<PathBuf> {
    let mut candidates = Vec::new();
    if let Ok(path) = std::env::var("STEAM_DIR") { candidates.push(PathBuf::from(path)); }
    if cfg!(target_os = "windows") {
        if let Ok(program_files) = std::env::var("PROGRAMFILES(X86)") {
            candidates.push(PathBuf::from(program_files).join("Steam"));
        }
        if let Ok(program_files) = std::env::var("PROGRAMFILES") {
            candidates.push(PathBuf::from(program_files).join("Steam"));
        }
    } else if cfg!(target_os = "linux") {
        if let Ok(home) = std::env::var("HOME") {
            candidates.push(PathBuf::from(&home).join(".steam/steam"));
            candidates.push(PathBuf::from(home).join(".local/share/Steam"));
        }
    }
    candidates.into_iter()
        .map(|root| root.join("steamapps/common/Hogwarts Legacy/Phoenix/Binaries/Win64/HogwartsLegacy.exe"))
        .find(|exe| exe.is_file())
}

fn main() -> eframe::Result<()> {
    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_inner_size([900.0, 820.0])
            .with_min_inner_size([680.0, 620.0]),
        ..Default::default()
    };
    eframe::run_native(
        "HogwartsRP Launcher",
        options,
        Box::new(|_creation_context| Ok(Box::new(HogwartsRpLauncher::default()))),
    )
}
