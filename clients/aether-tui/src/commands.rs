/// Commands handled locally by the TUI — never forwarded to server
const LOCAL_COMMANDS: &[&str] = &["/clear", "/help", "/quit", "/q", "/run", "/theme"];

#[derive(Debug, Clone, PartialEq)]
pub enum CommandAction {
    Local(LocalCommand),
    Forward(String), // JSON string to send
}

#[derive(Debug, Clone, PartialEq)]
pub enum LocalCommand {
    Clear,
    Help,
    Quit,
    Run(String),
    Theme(String),
}

pub fn dispatch(input: &str, group: &str) -> Option<CommandAction> {
    if input.starts_with('!') {
        let cmd = input[1..].trim().to_string();
        return Some(CommandAction::Local(LocalCommand::Run(cmd)));
    }
    if input.starts_with("/run ") {
        let cmd = input[5..].trim().to_string();
        return Some(CommandAction::Local(LocalCommand::Run(cmd)));
    }
    if !input.starts_with('/') {
        return None; // not a slash command
    }
    let cmd_lower = input.split_whitespace().next().unwrap_or("").to_lowercase();
    match cmd_lower.as_str() {
        "/clear" => Some(CommandAction::Local(LocalCommand::Clear)),
        "/help"  => Some(CommandAction::Local(LocalCommand::Help)),
        "/quit" | "/q" => Some(CommandAction::Local(LocalCommand::Quit)),
        "/theme" => {
            let name = input.split_whitespace().nth(1).unwrap_or("").to_string();
            Some(CommandAction::Local(LocalCommand::Theme(name)))
        }
        _ => {
            // Forward to server
            let json = serde_json::json!({
                "type": "command",
                "text": input,
                "group": group
            });
            Some(CommandAction::Forward(json.to_string()))
        }
    }
}

// Suppress unused warning for LOCAL_COMMANDS array (used conceptually)
#[allow(dead_code)]
fn _use_local_commands() -> &'static [&'static str] {
    LOCAL_COMMANDS
}
