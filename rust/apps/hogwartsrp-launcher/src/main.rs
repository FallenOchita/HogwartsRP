pub mod launcher_core;
pub mod oidc;
pub mod network;

use directories::ProjectDirs;
use eframe::egui;
use serde::{Deserialize, Serialize};
use std::{
    fs,
    io::Write,
    path::PathBuf,
    sync::{mpsc::{self, Receiver}, Arc, Mutex},
    thread,
    time::Duration,
};
use url::Url;

#[derive(Clone, Debug, Serialize, Deserialize)]
struct ServerProfile {
    name: String,
    address: String,
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
    join_credential: String,
    connection: Arc<Mutex<Option<network::WebSocketPlayerNetworkClient>>>,
    character_name: String,
    character_house: String,
    character_background: String,
    character_biography: String,
    wand_wood: String,
    wand_core: String,
    wand_length: String,
    wand_flexibility: String,
}

impl Default for HogwartsRpLauncher {
    fn default() -> Self {
        let saved_servers = load_profiles();
        let account_portal = std::env::var("HOGWARTSRP_ACCOUNT_PORTAL_URL").unwrap_or_default();
        Self {
            server_name: "Local development".into(),
            server_address: "http://127.0.0.1:7777".into(),
            saved_servers,
            selected_server: None,
            status_message: "Choose a server to begin.".into(),
            server_status: "Not checked".into(),
            status_receiver: None,
            account_portal,
            join_credential: String::new(),
            connection: Arc::new(Mutex::new(None)),
            character_name: String::new(),
            character_house: "Gryffindor".into(),
            character_background: String::new(),
            character_biography: String::new(),
            wand_wood: String::new(),
            wand_core: String::new(),
            wand_length: String::new(),
            wand_flexibility: String::new(),
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
                let status = launcher_core::ServerStatusClient::default().get_status(&address)?;
                Ok(format!("{} · {}/{} players", status.server_name, status.online_players, status.max_players))
            })();
            let _ = sender.send(result);
        });
    }

    fn launch_game(&mut self) {
        let additional_roots = std::env::var_os("HOGWARTSRP_GAME_ROOTS")
            .map(|value| std::env::split_paths(&value).collect())
            .unwrap_or_default();
        match launcher_core::GameInstallationDetector::new(additional_roots).detect() {
            Some(installation) => match launcher_core::launch_steam(&installation) {
                Ok(()) => self.status_message = "Steam launch request sent. Game startup is not yet confirmed.".into(),
                Err(error) => self.status_message = format!("Could not launch Hogwarts Legacy through Steam: {error}"),
            },
            None => self.status_message = "A supported Hogwarts Legacy installation was not found. Configure HOGWARTSRP_GAME_ROOTS to add custom install directories.".into(),
        }
    }

    fn connect_server(&mut self) {
        let address = self.server_address.trim().to_owned();
        let issuer = std::env::var("HOGWARTSRP_OIDC_ISSUER").unwrap_or_default();
        let client_id = std::env::var("HOGWARTSRP_OIDC_CLIENT_ID").unwrap_or_default();
        let connection = Arc::clone(&self.connection);
        let (sender, receiver) = mpsc::channel();
        self.status_receiver = Some(receiver);
        self.server_status = "Signing in…".into();
        self.status_message = "Complete sign-in in your browser to connect to the selected server.".into();
        thread::spawn(move || {
            let result = (|| {
                let provider = oidc::OidcJoinCredentialProvider::new(&issuer, &client_id)?;
                let credential = provider.get_join_credential(&address)?;
                let client = network::WebSocketPlayerNetworkClient::connect(&address, &credential)?;
                let display_name = client.session().display_name.clone();
                let mut slot = connection.lock().map_err(|_| "Launcher connection state is unavailable.".to_owned())?;
                if let Some(mut previous) = slot.take() { previous.disconnect(); }
                *slot = Some(client);
                Ok(format!("Connected as {display_name}."))
            })();
            let _ = sender.send(result);
        });
    }

    fn connect_with_ticket(&mut self) {
        let address = self.server_address.trim().to_owned();
        let credential = self.join_credential.trim().to_owned();
        if credential.is_empty() {
            self.status_message = "Enter a one-time development ticket first.".into();
            return;
        }
        let connection = Arc::clone(&self.connection);
        let (sender, receiver) = mpsc::channel();
        self.status_receiver = Some(receiver);
        self.server_status = "Connecting…".into();
        thread::spawn(move || {
            let result = (|| {
                let client = network::WebSocketPlayerNetworkClient::connect(&address, &credential)?;
                let display_name = client.session().display_name.clone();
                let mut slot = connection.lock().map_err(|_| "Launcher connection state is unavailable.".to_owned())?;
                if let Some(mut previous) = slot.take() { previous.disconnect(); }
                *slot = Some(client);
                Ok(format!("Connected as {display_name} using a development ticket."))
            })();
            let _ = sender.send(result);
        });
    }

    fn disconnect_server(&mut self) {
        match self.connection.lock() {
            Ok(mut slot) => {
                if let Some(mut client) = slot.take() { client.disconnect(); }
                self.server_status = "Disconnected".into();
                self.status_message = "Disconnected from the selected server.".into();
            }
            Err(_) => self.status_message = "Could not access the launcher connection.".into(),
        }
    }

    fn submit_character(&mut self) {
        let wand_length = if self.wand_length.trim().is_empty() {
            None
        } else {
            match self.wand_length.trim().parse::<f64>() {
                Ok(value) => Some(value),
                Err(_) => {
                    self.status_message = "Wand length must be a number between 4 and 16 inches.".into();
                    return;
                }
            }
        };
        let draft = launcher_core::CharacterCreationDraft {
            name: self.character_name.clone(),
            house: self.character_house.clone(),
            background: (!self.character_background.trim().is_empty()).then(|| self.character_background.clone()),
            biography: (!self.character_biography.trim().is_empty()).then(|| self.character_biography.clone()),
            wand_wood: (!self.wand_wood.trim().is_empty()).then(|| self.wand_wood.clone()),
            wand_core: (!self.wand_core.trim().is_empty()).then(|| self.wand_core.clone()),
            wand_length,
            wand_flexibility: (!self.wand_flexibility.trim().is_empty()).then(|| self.wand_flexibility.clone()),
        };
        let submission = match draft.try_build_submission() {
            Ok(value) => value,
            Err(errors) => {
                self.status_message = format!("Character was not submitted: {}", errors.join(" "));
                return;
            }
        };
        let connection = Arc::clone(&self.connection);
        let (sender, receiver) = mpsc::channel();
        self.status_receiver = Some(receiver);
        self.status_message = "Submitting character to the selected server…".into();
        thread::spawn(move || {
            let result = (|| {
                let mut slot = connection.lock().map_err(|_| "Launcher connection state is unavailable.".to_owned())?;
                let client = slot.as_mut().ok_or_else(|| "Connect to a server before submitting a character.".to_owned())?;
                let response = client.submit_character(&submission)?;
                if !response.success {
                    return Err(response.error.unwrap_or_else(|| "The server rejected the character.".into()));
                }
                let name = response.character.as_ref().map(|character| character.name.as_str()).unwrap_or(&submission.name);
                Ok(format!("Character registered: {name}. This stores RP data on the server; it does not create an in-game character."))
            })();
            let _ = sender.send(result);
        });
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

    fn poll_status(&mut self, ctx: &egui::Context) {
        let Some(receiver) = &self.status_receiver else { return; };
        match receiver.try_recv() {
            Ok(Ok(status)) => {
                self.server_status = status.clone();
                self.status_message = status;
                self.status_receiver = None;
            }
            Ok(Err(error)) => {
                self.server_status = "Unavailable".into();
                self.status_message = format!("Background launcher operation failed: {error}");
                self.status_receiver = None;
            }
            Err(mpsc::TryRecvError::Disconnected) => {
                self.server_status = "Unavailable".into();
                self.status_message = "Launcher background operation stopped unexpectedly.".into();
                self.status_receiver = None;
            }
            Err(mpsc::TryRecvError::Empty) => ctx.request_repaint_after(Duration::from_millis(100)),
        }
    }
}

impl eframe::App for HogwartsRpLauncher {
    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        self.poll_status(ctx);
        egui::CentralPanel::default().show(ctx, |ui| {
            ui.heading("HOGWARTSRP");
            ui.label("Native Rust launcher · Hogwarts Legacy roleplay framework");
            ui.add_space(14.0);

            ui.group(|ui| {
                ui.heading("Server");
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

            ui.horizontal(|ui| {
                if ui.button("Connect / Sign in").clicked() { self.connect_server(); }
                if ui.button("Disconnect").clicked() { self.disconnect_server(); }
            });
            ui.horizontal(|ui| {
                ui.label("Loopback development ticket");
                ui.text_edit_singleline(&mut self.join_credential);
                if ui.button("Connect with ticket").clicked() { self.connect_with_ticket(); }
            });

            ui.add_space(10.0);
            ui.group(|ui| {
                ui.heading("Roleplay character");
                ui.horizontal(|ui| {
                    ui.label("Name");
                    ui.text_edit_singleline(&mut self.character_name);
                    ui.label("House");
                    egui::ComboBox::from_id_salt("character-house")
                        .selected_text(&self.character_house)
                        .show_ui(ui, |ui| {
                            for house in ["Gryffindor", "Hufflepuff", "Ravenclaw", "Slytherin"] {
                                ui.selectable_value(&mut self.character_house, house.to_owned(), house);
                            }
                        });
                });
                ui.label("Background");
                ui.text_edit_multiline(&mut self.character_background);
                ui.label("Biography");
                ui.text_edit_multiline(&mut self.character_biography);
                ui.horizontal(|ui| {
                    ui.label("Wand wood");
                    ui.text_edit_singleline(&mut self.wand_wood);
                    ui.label("Core");
                    ui.text_edit_singleline(&mut self.wand_core);
                });
                ui.horizontal(|ui| {
                    ui.label("Length (in)");
                    ui.text_edit_singleline(&mut self.wand_length);
                    ui.label("Flexibility");
                    ui.text_edit_singleline(&mut self.wand_flexibility);
                });
                if ui.button("Submit roleplay character").clicked() { self.submit_character(); }
                ui.small("The server validates and stores roleplay metadata; this does not create a Hogwarts Legacy game character.");
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
            ui.small("Migration notice: this Rust executable replaces the WinForms launcher shell. Authenticated server connection, character submission, custom Steam library discovery, launcher auto-update, and the public server directory still need their Rust implementations; this build does not invoke the legacy C# launcher.");
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


fn main() -> eframe::Result<()> {
    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_inner_size([820.0, 700.0])
            .with_min_inner_size([640.0, 560.0]),
        ..Default::default()
    };
    eframe::run_native(
        "HogwartsRP Launcher",
        options,
        Box::new(|_creation_context| Ok(Box::new(HogwartsRpLauncher::default()))),
    )
}
