//! Synchronous authenticated WebSocket client for the HogwartsRP launcher.
//! Run blocking operations on a background thread; the server remains authoritative for identity and validation.

use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::{
    collections::VecDeque,
    net::TcpStream,
};
use tungstenite::{connect, stream::MaybeTlsStream, Message, WebSocket};
use uuid::Uuid;

use crate::launcher_core::{validate_server_endpoint, CharacterSubmission};

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PlayerSession {
    pub id: String,
    pub display_name: String,
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RegisteredCharacter {
    pub id: String,
    pub owner_player_id: String,
    pub name: String,
    pub house: String,
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CharacterSubmissionResult {
    pub success: bool,
    pub character: Option<RegisteredCharacter>,
    pub error: Option<String>,
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ServerNetworkEvent {
    pub name: String,
    #[serde(default)]
    pub arguments: Vec<Value>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct WireMessage<'a> {
    #[serde(rename = "type")]
    message_type: &'a str,
    #[serde(skip_serializing_if = "Option::is_none")]
    credential: Option<&'a str>,
    #[serde(skip_serializing_if = "Option::is_none")]
    name: Option<&'a str>,
    #[serde(skip_serializing_if = "Option::is_none")]
    arguments: Option<&'a [Value]>,
    #[serde(skip_serializing_if = "Option::is_none")]
    request_id: Option<&'a str>,
    #[serde(skip_serializing_if = "Option::is_none")]
    submission: Option<&'a CharacterSubmission>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct IncomingMessage {
    #[serde(rename = "type")]
    message_type: String,
    player_id: Option<String>,
    display_name: Option<String>,
    error: Option<String>,
    request_id: Option<String>,
    submission_result: Option<CharacterSubmissionResult>,
    name: Option<String>,
    arguments: Option<Vec<Value>>,
}

pub struct WebSocketPlayerNetworkClient {
    socket: WebSocket<MaybeTlsStream<TcpStream>>,
    session: PlayerSession,
    pending_events: VecDeque<ServerNetworkEvent>,
    connected: bool,
}

impl WebSocketPlayerNetworkClient {
    pub fn connect(server_endpoint: &str, credential: &str) -> Result<Self, String> {
        if credential.trim().is_empty() {
            return Err("A one-time join credential is required.".into());
        }
        let mut endpoint = validate_server_endpoint(server_endpoint)?;
        endpoint.set_scheme(match endpoint.scheme() {
            "https" | "wss" => "wss",
            "http" | "ws" => "ws",
            _ => return Err("Unsupported WebSocket endpoint scheme.".into()),
        }).map_err(|_| "Invalid WebSocket URL scheme.".to_owned())?;
        endpoint.set_path("/connect");
        endpoint.set_query(None);
        endpoint.set_fragment(None);

        let (mut socket, _) = connect(endpoint.as_str()).map_err(|error| format!("WebSocket connection failed: {error}"))?;
        send_json(&mut socket, &WireMessage {
            message_type: "authenticate",
            credential: Some(credential),
            name: None,
            arguments: None,
            request_id: None,
            submission: None,
        })?;

        let response = read_json(&mut socket)?;
        if response.message_type == "error" {
            return Err(response.error.unwrap_or_else(|| "Server rejected the join credential.".into()));
        }
        if response.message_type != "connected"
            || response.player_id.as_deref().is_none_or(str::is_empty)
            || response.display_name.as_deref().is_none_or(str::is_empty)
            || response.display_name.as_ref().is_some_and(|name| name.chars().count() > 64) {
            return Err("Server returned an invalid authenticated session handshake.".into());
        }
        let session = PlayerSession {
            id: response.player_id.expect("validated above"),
            display_name: response.display_name.expect("validated above"),
        };
        Ok(Self { socket, session, pending_events: VecDeque::new(), connected: true })
    }

    pub fn session(&self) -> &PlayerSession { &self.session }
    pub fn is_connected(&self) -> bool { self.connected }

    pub fn send_event(&mut self, event_name: &str, arguments: &[Value]) -> Result<(), String> {
        validate_event(event_name)?;
        validate_arguments(arguments)?;
        send_json(&mut self.socket, &WireMessage {
            message_type: "event",
            credential: None,
            name: Some(event_name),
            arguments: Some(arguments),
            request_id: None,
            submission: None,
        })
    }

    pub fn submit_character(&mut self, submission: &CharacterSubmission) -> Result<CharacterSubmissionResult, String> {
        if !self.connected { return Err("The launcher is not connected to a server.".into()); }
        let request_id = Uuid::new_v4().simple().to_string();
        send_json(&mut self.socket, &WireMessage {
            message_type: "submit_character",
            credential: None,
            name: None,
            arguments: None,
            request_id: Some(&request_id),
            submission: Some(submission),
        })?;
        loop {
            let incoming = read_json(&mut self.socket)?;
            match incoming.message_type.as_str() {
                "character_submission_result" if incoming.request_id.as_deref() == Some(&request_id) => {
                    return incoming.submission_result.ok_or_else(|| "Server returned an empty character submission result.".into());
                }
                "event" => {
                    if let Some(name) = incoming.name {
                        validate_event(&name)?;
                        let arguments = incoming.arguments.unwrap_or_default();
                        validate_arguments(&arguments)?;
                        self.pending_events.push_back(ServerNetworkEvent { name, arguments });
                    }
                }
                "event_rejected" | "event_error" | "error" => {
                    return Err(incoming.error.unwrap_or_else(|| format!("Server response: {}", incoming.message_type)));
                }
                _ => {}
            }
        }
    }

    pub fn take_pending_event(&mut self) -> Option<ServerNetworkEvent> {
        self.pending_events.pop_front()
    }

    pub fn disconnect(&mut self) {
        if self.connected {
            let _ = self.socket.close(None);
            self.connected = false;
        }
    }
}

fn send_json(socket: &mut WebSocket<MaybeTlsStream<TcpStream>>, message: &impl Serialize) -> Result<(), String> {
    let json = serde_json::to_string(message).map_err(|error| error.to_string())?;
    if json.len() > 32 * 1024 { return Err("WebSocket message exceeds the 32 KiB protocol limit.".into()); }
    socket.send(Message::Text(json.into())).map_err(|error| error.to_string())
}

fn read_json(socket: &mut WebSocket<MaybeTlsStream<TcpStream>>) -> Result<IncomingMessage, String> {
    loop {
        match socket.read().map_err(|error| error.to_string())? {
            Message::Text(text) => {
                if text.len() > 32 * 1024 { return Err("Server message exceeds the 32 KiB protocol limit.".into()); }
                return serde_json::from_str(text.as_ref()).map_err(|error| error.to_string());
            }
            Message::Ping(payload) => socket.send(Message::Pong(payload)).map_err(|error| error.to_string())?,
            Message::Close(_) => return Err("Server closed the WebSocket connection.".into()),
            Message::Binary(_) => return Err("Only UTF-8 JSON text messages are supported.".into()),
            _ => {}
        }
    }
}

fn validate_event(name: &str) -> Result<(), String> {
    let mut chars = name.chars();
    let first = chars.next().ok_or_else(|| "Event name cannot be empty.".to_owned())?;
    if !first.is_ascii_alphabetic() || name.len() > 128
        || !chars.all(|c| c.is_ascii_alphanumeric() || matches!(c, '.' | '_' | ':' | '-')) {
        return Err("Invalid network event name.".into());
    }
    Ok(())
}

fn validate_arguments(arguments: &[Value]) -> Result<(), String> {
    if arguments.len() > 32 { return Err("Network events may contain at most 32 arguments.".into()); }
    for value in arguments {
        match value {
            Value::Null | Value::Bool(_) => {}
            Value::Number(number) if number.as_f64().is_some_and(f64::is_finite) => {}
            Value::String(text) if text.chars().count() <= 8_192 => {}
            _ => return Err("Network event arguments must be finite primitive values; strings are limited to 8192 characters.".into()),
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn validates_network_event_names_and_arguments() {
        assert!(validate_event("player:joined").is_ok());
        assert!(validate_event("../unsafe").is_err());
        assert!(validate_arguments(&[Value::String("hello".into()), Value::Bool(true)]).is_ok());
        assert!(validate_arguments(&[serde_json::json!({"nested": true})]).is_err());
        assert!(validate_arguments(&vec![Value::Null; 33]).is_err());
    }

    #[test]
    fn endpoint_conversion_keeps_remote_transport_secure() {
        let endpoint = validate_server_endpoint("https://example.test").unwrap();
        assert_eq!(endpoint.scheme(), "https");
        assert!(validate_server_endpoint("http://example.test").is_err());
    }
}
