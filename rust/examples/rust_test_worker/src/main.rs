use hogwartsrp_resource_sdk::Resource;
use serde_json::{json, Value};

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let mut resource = Resource::new("integration_test")?;
    resource.register_command("hello", |arguments: &[Value]| {
        let name = arguments.first().and_then(Value::as_str).unwrap_or("world");
        Ok(Some(json!(format!("Hello, {name}!"))))
    })?;
    resource.register_command("exit", |arguments: &[Value]| {
        let code = arguments.first().and_then(Value::as_u64).unwrap_or(1).min(255) as i32;
        std::process::exit(code);
    })?;
    resource.on_event("player:joined", |arguments| {
        eprintln!("player:joined received ({} argument(s))", arguments.len());
        Ok(None)
    })?;
    resource.register_network_event("player:joined")?;
    resource.run()?;
    Ok(())
}
