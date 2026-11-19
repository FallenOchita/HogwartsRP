use hogwartsrp_resource_sdk::Resource;
use serde_json::Value;

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let mut resource = Resource::new("rust_greeter")?;
    resource.register_command("hello", |arguments: &[Value]| {
        let who = arguments.first().and_then(Value::as_str).unwrap_or("witch or wizard");
        eprintln!("Hello, {who}!");
        Ok(Some(Value::String(format!("Hello, {who}!"))))
    })?;
    resource.on_event("player:joined", |arguments| {
        eprintln!("player:joined received ({} argument(s))", arguments.len());
        Ok(None)
    })?;
    resource.register_network_event("player:joined")?;
    resource.run()?;
    Ok(())
}
