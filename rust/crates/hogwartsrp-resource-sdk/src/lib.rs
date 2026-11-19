//! JSON-lines worker SDK for standalone Rust HogwartsRP resources.
//! The host owns identity, permissions, networking, and persistence.

use hogwartsrp_native_core::{validate_event_argument_count, validate_event_name, validate_protocol_frame, validate_resource_name};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::collections::HashMap;
use std::io::{self, BufRead, Write};

const MAX_LINE_BYTES: usize = 32 * 1024;

#[derive(Debug, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
struct WorkerMessage {
    #[serde(rename = "type")]
    kind: String,
    #[serde(default)]
    id: Option<String>,
    #[serde(default)]
    name: Option<String>,
    #[serde(default)]
    event_name: Option<String>,
    #[serde(default)]
    arguments: Vec<Value>,
    #[serde(default)]
    error: Option<String>,
}

type Handler = Box<dyn Fn(&[Value]) -> Result<Option<Value>, String> + Send + Sync>;

#[derive(Default)]
pub struct Resource {
    name: Option<String>,
    commands: HashMap<String, Handler>,
    events: HashMap<String, Vec<Handler>>,
    network_events: Vec<String>,
}

impl Resource {
    pub fn new(name: impl Into<String>) -> Result<Self, String> {
        let name = name.into();
        validate_resource_name(&name).map_err(|e| format!("invalid resource name: {e:?}"))?;
        Ok(Self { name: Some(name), ..Self::default() })
    }

    pub fn register_command<F>(&mut self, name: impl Into<String>, handler: F) -> Result<(), String>
    where F: Fn(&[Value]) -> Result<Option<Value>, String> + Send + Sync + 'static {
        let name = name.into();
        validate_event_name(&name).map_err(|e| format!("invalid command name: {e:?}"))?;
        if self.commands.insert(name.clone(), Box::new(handler)).is_some() {
            return Err(format!("command already registered: {name}"));
        }
        Ok(())
    }

    pub fn on_event<F>(&mut self, name: impl Into<String>, handler: F) -> Result<(), String>
    where F: Fn(&[Value]) -> Result<Option<Value>, String> + Send + Sync + 'static {
        let name = name.into();
        validate_event_name(&name).map_err(|e| format!("invalid event name: {e:?}"))?;
        self.events.entry(name).or_default().push(Box::new(handler));
        Ok(())
    }

    /// Declares an inbound event; the server must enforce the allowlist and identity.
    pub fn register_network_event(&mut self, name: impl Into<String>) -> Result<(), String> {
        let name = name.into();
        validate_event_name(&name).map_err(|e| format!("invalid event name: {e:?}"))?;
        if !self.network_events.contains(&name) { self.network_events.push(name); }
        Ok(())
    }

    /// Runs on stdin/stdout. stdout is reserved for protocol messages; use stderr for logs.
    pub fn run(self) -> Result<(), String> {
        let _resource_name = self.name.as_deref().unwrap_or("unnamed");
        let stdout = io::stdout();
        let mut output = stdout.lock();
        for name in self.commands.keys() {
            write_message(&mut output, &WorkerMessage { kind: "command_registered".into(), id: None,
                name: Some(name.clone()), event_name: None, arguments: vec![], error: None })?;
        }
        for name in &self.network_events {
            write_message(&mut output, &WorkerMessage { kind: "network_event_registered".into(), id: None,
                name: Some(name.clone()), event_name: None, arguments: vec![], error: None })?;
        }
        write_message(&mut output, &WorkerMessage { kind: "ready".into(), id: None, name: None,
            event_name: None, arguments: vec![], error: None })?;

        let stdin = io::stdin();
        for line in stdin.lock().lines() {
            let line = line.map_err(|e| e.to_string())?;
            if line.len() > MAX_LINE_BYTES { return Err("host message exceeded worker protocol limit".into()); }
            validate_protocol_frame(line.as_bytes()).map_err(|e| format!("invalid frame: {e:?}"))?;
            let message: WorkerMessage = serde_json::from_str(&line).map_err(|e| e.to_string())?;
            if message.kind == "stop" { break; }
            validate_event_argument_count(message.arguments.len()).map_err(|e| format!("invalid argument count: {e:?}"))?;
            let result = match message.kind.as_str() {
                "invoke_command" => {
                    let name = message.name.as_deref().unwrap_or_default();
                    self.commands.get(name).ok_or_else(|| format!("unknown command: {name}"))
                        .and_then(|handler| handler(&message.arguments))
                }
                "dispatch_event" => {
                    let name = message.event_name.as_deref().unwrap_or_default();
                    if let Some(handlers) = self.events.get(name) {
                        for handler in handlers { handler(&message.arguments)?; }
                    }
                    Ok(None)
                }
                _ => Err(format!("unsupported worker request: {}", message.kind)),
            };
            let response = match result {
                Ok(value) => WorkerMessage { kind: "request_result".into(), id: message.id, name: None,
                    event_name: None, arguments: value.into_iter().collect(), error: None },
                Err(error) => WorkerMessage { kind: "request_error".into(), id: message.id, name: None,
                    event_name: None, arguments: vec![], error: Some(error) },
            };
            write_message(&mut output, &response)?;
        }
        Ok(())
    }
}

fn write_message(output: &mut impl Write, message: &WorkerMessage) -> Result<(), String> {
    serde_json::to_writer(&mut *output, message).map_err(|e| e.to_string())?;
    output.write_all(b"\n").map_err(|e| e.to_string())?;
    output.flush().map_err(|e| e.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn rejects_invalid_resource_and_event_names() {
        assert!(Resource::new("../unsafe").is_err());
        let mut resource = Resource::new("example").unwrap();
        assert!(resource.register_network_event("bad event").is_err());
    }
    #[test]
    fn prevents_duplicate_commands() {
        let mut resource = Resource::new("example").unwrap();
        resource.register_command("ping", |_| Ok(None)).unwrap();
        assert!(resource.register_command("ping", |_| Ok(None)).is_err());
    }
    #[test]
    fn accepts_registered_event_handlers() {
        let mut resource = Resource::new("example").unwrap();
        assert!(resource.on_event("resource:started", |_| Ok(None)).is_ok());
    }
}
