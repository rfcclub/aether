mod config;
mod events;
mod ws;
mod app;
mod ui;
mod commands;

use config::Config;
use events::{AppEvent, Message, Role};
use ws::ws_task;
use app::{AppMode, AppState};
use chrono::Utc;

use clap::Parser;
use crossterm::event::{KeyCode, KeyModifiers, MouseEventKind};
use ratatui::{backend::CrosstermBackend, Terminal};
use tokio::sync::mpsc;

#[derive(Parser)]
#[command(name = "aether-tui", about = "Terminal UI for Aether AI")]
struct Args {
    /// WebSocket URL override (e.g. ws://localhost:5099/ws)
    #[arg(long)]
    url: Option<String>,

    /// Agent group to connect to
    #[arg(long, default_value = "maria")]
    group: String,

    /// Agent name to connect to (alias for group)
    #[arg(long, short = 'a')]
    agent: Option<String>,
}

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    let mut args = Args::parse();
    if let Some(agent) = args.agent {
        args.group = agent;
    }
    let config = Config::resolve(args.url.clone(), args.group.clone());

    // Setup terminal
    crossterm::terminal::enable_raw_mode()?;
    let mut stdout = std::io::stdout();
    crossterm::execute!(
        stdout,
        crossterm::terminal::EnterAlternateScreen,
        crossterm::event::EnableMouseCapture
    )?;

    // Panic hook: always restore terminal
    let default_hook = std::panic::take_hook();
    std::panic::set_hook(Box::new(move |info| {
        let _ = crossterm::terminal::disable_raw_mode();
        let _ = crossterm::execute!(
            std::io::stdout(),
            crossterm::terminal::LeaveAlternateScreen,
            crossterm::event::DisableMouseCapture
        );
        default_hook(info);
    }));

    let backend = CrosstermBackend::new(stdout);
    let mut terminal = Terminal::new(backend)?;

    // Channels
    let (app_tx, mut app_rx) = mpsc::channel::<AppEvent>(256);
    let (mut ws_out_tx, ws_out_rx) = mpsc::channel::<String>(64);

    // Spawn WS task (Phase 2: pass group for get_history)
    let ws_url = config.ws_url.clone();
    let ws_group = config.group.clone();
    let ws_tx = app_tx.clone();
    tokio::spawn(ws_task(ws_url, ws_group, ws_tx, ws_out_rx));

    let mut state = AppState::new(config.group.clone());
    let mut last_key_was_g = false;
    let mut last_tick = std::time::Instant::now();

    loop {
        // Update spinner frame based on actual time elapsed to prevent animation freezing
        let now = std::time::Instant::now();
        let elapsed = now.duration_since(last_tick);
        if elapsed >= std::time::Duration::from_millis(16) {
            let frames = (elapsed.as_millis() / 16) as u8;
            if frames > 0 {
                state.spinner_frame = (state.spinner_frame + frames) % 240;
                last_tick = now;
            }
        }

        // Render
        terminal.draw(|f| ui::draw(f, &state))?;

        // Event handling with 16ms timeout (≈60fps)
        let timeout = tokio::time::Duration::from_millis(16);
        tokio::select! {
            // App events (WS messages)
            Some(event) = app_rx.recv() => {
                if state.handle_event(event) { break; }
            }
            // Sleep fallback to yield CPU time
            _ = tokio::time::sleep(timeout) => {}
        }

        // Poll terminal events on every iteration to keep UI completely responsive
        while crossterm::event::poll(std::time::Duration::ZERO)? {
            match crossterm::event::read()? {
                crossterm::event::Event::Key(key) => {
                    if let Some(new_agent) = handle_key(&mut state, key, &ws_out_tx, &app_tx, &mut last_key_was_g).await {
                        let (new_ws_out_tx, ws_out_rx) = mpsc::channel::<String>(64);
                        ws_out_tx = new_ws_out_tx;

                        state.messages.clear();
                        state.streaming_buf.clear();
                        state.group = new_agent.clone();
                        state.history_loaded = false;
                        state.connected = false;
                        state.mode = AppMode::Connecting;

                        let new_config = Config::resolve(args.url.clone(), new_agent.clone());
                        let ws_url = new_config.ws_url.clone();
                        let ws_group = new_config.group.clone();
                        let ws_tx = app_tx.clone();
                        tokio::spawn(ws_task(ws_url, ws_group, ws_tx, ws_out_rx));
                    }
                    if state.mode == AppMode::Connecting { /* keep going */ }
                }
                crossterm::event::Event::Mouse(mouse) => {
                    handle_mouse(&mut state, mouse);
                }
                crossterm::event::Event::Resize(_, _) => {
                    // Just let the next draw call handle resize
                }
                _ => {}
            }
        }

        // Check if app should quit
        if state.mode == AppMode::Normal && false {
            // placeholder
            break;
        }
    }

    // Restore terminal
    crossterm::terminal::disable_raw_mode()?;
    crossterm::execute!(
        terminal.backend_mut(),
        crossterm::terminal::LeaveAlternateScreen,
        crossterm::event::DisableMouseCapture
    )?;
    terminal.show_cursor()?;
    Ok(())
}

async fn handle_key(
    state: &mut AppState,
    key: crossterm::event::KeyEvent,
    ws_out_tx: &mpsc::Sender<String>,
    app_tx: &mpsc::Sender<AppEvent>,
    last_key_was_g: &mut bool,
) -> Option<String> {
    match state.mode {
        AppMode::ShowHelp => {
            // Any key closes help
            state.mode = AppMode::Normal;
            return None;
        }
        AppMode::Normal | AppMode::Connecting => {
            *last_key_was_g = false;
            match (key.code, key.modifiers) {
                // Send message on Enter, or insert newline on Shift+Enter, Ctrl+Enter, Alt+Enter
                (KeyCode::Enter, mods) => {
                    if mods.contains(KeyModifiers::SHIFT) || mods.contains(KeyModifiers::ALT) || mods.contains(KeyModifiers::CONTROL) {
                        state.insert_char('\n');
                    } else {
                        let input = state.input.trim().to_string();
                        if !input.is_empty() {
                        // Check for slash commands first
                        if let Some(action) = commands::dispatch(&input, &state.group) {
                            state.input.clear();
                            match action {
                                commands::CommandAction::Local(local_cmd) => {
                                    match local_cmd {
                                        commands::LocalCommand::Clear => {
                                            state.messages.clear();
                                            state.streaming_buf.clear();
                                        }
                                        commands::LocalCommand::Help => {
                                            state.mode = AppMode::ShowHelp;
                                        }
                                        commands::LocalCommand::Quit => {
                                            let _ = app_tx.send(AppEvent::Quit).await;
                                        }
                                        commands::LocalCommand::Run(cmd) => {
                                            // 1. Show the command in chat
                                            state.messages.push(Message {
                                                role: Role::User,
                                                content: input.clone(),
                                                timestamp: Utc::now(),
                                                is_historical: false,
                                            });
                                            
                                            // 2. Set as typing for assistant to show spinner
                                            state.is_typing = true;
                                            
                                            // 3. Spawn a background tokio thread to execute
                                            let app_tx_clone = app_tx.clone();
                                            let ws_out_tx_clone = ws_out_tx.clone();
                                            let group = state.group.clone();
                                            tokio::spawn(async move {
                                                let prefix = format!("⚙️ [bash] Running: {}\n\n", cmd);
                                                let _ = app_tx_clone.send(AppEvent::StreamChunk(prefix.clone())).await;
                                                
                                                let mut accumulated_output = prefix;
                                                
                                                let mut child = match tokio::process::Command::new("sh")
                                                    .arg("-c")
                                                    .arg(format!("{} 2>&1", cmd))
                                                    .stdout(std::process::Stdio::piped())
                                                    .spawn() 
                                                {
                                                    Ok(child) => child,
                                                    Err(e) => {
                                                        let err_msg = format!("❌ Failed to spawn command: {}\n", e);
                                                        let _ = app_tx_clone.send(AppEvent::StreamChunk(err_msg.clone())).await;
                                                        accumulated_output.push_str(&err_msg);
                                                        let _ = app_tx_clone.send(AppEvent::MessageComplete(accumulated_output)).await;
                                                        return;
                                                    }
                                                };
                                                
                                                if let Some(stdout) = child.stdout.take() {
                                                    use tokio::io::AsyncBufReadExt;
                                                    let mut reader = tokio::io::BufReader::new(stdout);
                                                    let mut line = String::new();
                                                    while let Ok(n) = reader.read_line(&mut line).await {
                                                        if n == 0 { break; }
                                                        let _ = app_tx_clone.send(AppEvent::StreamChunk(line.clone())).await;
                                                        accumulated_output.push_str(&line);
                                                        line.clear();
                                                    }
                                                }
                                                
                                                let status = child.wait().await;
                                                let exit_status = match status {
                                                    Ok(s) => {
                                                        if s.success() {
                                                            "\n✅ [bash] Completed successfully.".to_string()
                                                        } else {
                                                            format!("\n❌ [bash] Failed with exit code {}.", s.code().unwrap_or(1))
                                                        }
                                                    }
                                                    Err(e) => format!("\n❌ [bash] Failed to wait for process: {}.", e),
                                                };
                                                let _ = app_tx_clone.send(AppEvent::StreamChunk(exit_status.clone())).await;
                                                accumulated_output.push_str(&exit_status);
                                                
                                                // Complete stream locally
                                                let _ = app_tx_clone.send(AppEvent::MessageComplete(accumulated_output.clone())).await;
                                                
                                                // Send output context to Agent
                                                let backend_text = format!(
                                                    "Executed local shell command: `{}`\nResult:\n```\n{}\n```",
                                                    cmd,
                                                    accumulated_output
                                                );
                                                let backend_json = serde_json::json!({
                                                    "type": "message",
                                                    "text": backend_text,
                                                    "group": group
                                                });
                                                let _ = ws_out_tx_clone.send(backend_json.to_string()).await;
                                            });
                                        }
                                    }
                                }
                                commands::CommandAction::Forward(json) => {
                                    let _ = ws_out_tx.send(json).await;
                                }
                            }
                        } else {
                            // Regular message
                            if let Some(json) = state.send_message() {
                                let _ = ws_out_tx.send(json).await;
                            }
                        }
                    }
                }
            }
                // Ctrl+Q → quit
                (KeyCode::Char('q'), KeyModifiers::CONTROL) => {
                    let _ = app_tx.send(AppEvent::Quit).await;
                }
                // Ctrl+L → clear
                (KeyCode::Char('l'), KeyModifiers::CONTROL) => {
                    state.messages.clear();
                    state.streaming_buf.clear();
                }
                // Esc → cancel active streaming OR enter scroll mode
                (KeyCode::Esc, _) => {
                    if state.is_typing {
                        let cancel_req = serde_json::json!({
                            "type": "cancel",
                            "group": state.group
                        });
                        let _ = ws_out_tx.send(cancel_req.to_string()).await;
                        state.is_typing = false;
                        state.streaming_buf.push_str("\n🚫 [Generation Cancelled by User]");
                    } else {
                        state.mode = AppMode::Scroll;
                    }
                }
                // F1 → help
                (KeyCode::F(1), _) => {
                    state.mode = AppMode::ShowHelp;
                }
                // F2 or Ctrl+M → model picker
                (KeyCode::F(2), _) | (KeyCode::Char('m'), KeyModifiers::CONTROL) => {
                    state.mode = AppMode::ModelPicker;
                    state.picker_selection = 0;
                }
                // F3 or Ctrl+A → agent picker
                (KeyCode::F(3), _) | (KeyCode::Char('a'), KeyModifiers::CONTROL) => {
                    state.load_agents();
                    state.mode = AppMode::AgentPicker;
                    state.agent_selection = 0;
                }
                // F4 → context files manager
                (KeyCode::F(4), _) => {
                    state.mode = AppMode::ContextManager;
                    state.context_selection = 0;
                    state.show_input_dialog = false;
                }
                // F5 → brainstorming wizard
                (KeyCode::F(5), _) => {
                    state.mode = AppMode::BrainstormWizard;
                    state.brainstorm_step = 0;
                    state.brainstorm_answers = vec![String::new(); 4];
                }
                // F6 → TDD template injector
                (KeyCode::F(6), _) => {
                    let tdd_template = "🔄 TDD Step: [RED / GREEN / REFACTOR / COMMIT]\n- **Goal:** [Enter target goal]\n- **Files:**\n  - [MODIFY/NEW] [file name](file://absolute/path)\n- **Step Details:**\n  - [Write out detailed execution steps - NO PLACEHOLDERS]";
                    state.input = tdd_template.to_string();
                    state.cursor_position = state.input.chars().count();
                }
                // F7 → interactive git dashboard
                (KeyCode::F(7), _) => {
                    state.mode = AppMode::GitDashboard;
                    state.git_selection = 0;
                    state.selected_diff = "Loading diff...".to_string();
                    let req = serde_json::json!({
                        "type": "git_status",
                        "group": state.group
                    });
                    let _ = ws_out_tx.send(req.to_string()).await;
                }
                // Backspace
                (KeyCode::Backspace, _) => {
                    state.delete_backspace();
                }
                // Delete
                (KeyCode::Delete, _) => {
                    state.delete_char();
                }
                // Regular character input
                (KeyCode::Char(c), KeyModifiers::NONE) | (KeyCode::Char(c), KeyModifiers::SHIFT) => {
                    state.insert_char(c);
                }
                // Navigation keys
                (KeyCode::Left, _) => {
                    state.move_cursor_left();
                }
                (KeyCode::Right, _) => {
                    state.move_cursor_right();
                }
                (KeyCode::Up, _) => {
                    state.move_cursor_up();
                }
                (KeyCode::Down, _) => {
                    state.move_cursor_down();
                }
                (KeyCode::Home, _) => {
                    state.cursor_position = 0;
                }
                (KeyCode::End, _) => {
                    state.cursor_position = state.input.chars().count();
                }
                _ => {}
            }
        }
        AppMode::Scroll => {
            match (key.code, key.modifiers) {
                // Return to normal on Enter or i
                (KeyCode::Enter, _) | (KeyCode::Char('i'), _) => {
                    state.mode = AppMode::Normal;
                    state.scroll_offset = 0;
                    *last_key_was_g = false;
                }
                // Esc also returns to normal and resets offset
                (KeyCode::Esc, _) => {
                    state.mode = AppMode::Normal;
                    state.scroll_offset = 0;
                    *last_key_was_g = false;
                }
                // j scrolls toward older messages (increase offset = scroll up)
                (KeyCode::Char('j'), _) | (KeyCode::Down, _) => {
                    state.scroll_offset = state.scroll_offset.saturating_sub(1);
                    *last_key_was_g = false;
                }
                // k scrolls toward newer messages (decrease offset = scroll down toward bottom)
                (KeyCode::Char('k'), _) | (KeyCode::Up, _) => {
                    state.scroll_offset += 1; // clamped in ui.rs render
                    *last_key_was_g = false;
                }
                // PageUp: scroll many lines up (older)
                (KeyCode::PageUp, _) => {
                    state.scroll_offset += 10;
                    *last_key_was_g = false;
                }
                // PageDown: scroll many lines down (newer)
                (KeyCode::PageDown, _) => {
                    state.scroll_offset = state.scroll_offset.saturating_sub(10);
                    *last_key_was_g = false;
                }
                // G → jump to bottom
                (KeyCode::Char('G'), _) => {
                    state.scroll_offset = 0;
                    *last_key_was_g = false;
                }
                // g → double-g to jump to top
                (KeyCode::Char('g'), _) => {
                    if *last_key_was_g {
                        state.scroll_offset = usize::MAX; // ui.rs will clamp to actual max
                        *last_key_was_g = false;
                    } else {
                        *last_key_was_g = true;
                    }
                }
                // Ctrl+Q → quit
                (KeyCode::Char('q'), KeyModifiers::CONTROL) => {
                    let _ = app_tx.send(AppEvent::Quit).await;
                    *last_key_was_g = false;
                }
                // Any printable char: exit scroll mode and start typing
                (KeyCode::Char(c), KeyModifiers::NONE) | (KeyCode::Char(c), KeyModifiers::SHIFT) => {
                    state.mode = AppMode::Normal;
                    state.scroll_offset = 0;
                    state.input.push(c);
                    *last_key_was_g = false;
                }
                _ => {
                    *last_key_was_g = false;
                }
            }
        }
        AppMode::ModelPicker => {
            *last_key_was_g = false;
            match (key.code, key.modifiers) {
                // Navigate down
                (KeyCode::Char('j'), _) | (KeyCode::Down, _) => {
                    let max = state.total_selectable_models();
                    state.picker_selection = (state.picker_selection + 1).min(max.saturating_sub(1));
                }
                // Navigate up
                (KeyCode::Char('k'), _) | (KeyCode::Up, _) => {
                    state.picker_selection = state.picker_selection.saturating_sub(1);
                }
                // Select model
                (KeyCode::Enter, _) => {
                    if let Some(model) = state.selected_model() {
                        let json = serde_json::json!({
                            "type": "command",
                            "text": format!("/model {}", model),
                            "group": state.group
                        });
                        let _ = ws_out_tx.send(json.to_string()).await;
                    }
                    state.mode = AppMode::Normal;
                }
                // Close picker
                (KeyCode::Esc, _) => {
                    state.mode = AppMode::Normal;
                }
                _ => {}
            }
        }
        AppMode::AgentPicker => {
            *last_key_was_g = false;
            match (key.code, key.modifiers) {
                // Navigate down
                (KeyCode::Char('j'), _) | (KeyCode::Down, _) => {
                    if !state.agents.is_empty() {
                        state.agent_selection = (state.agent_selection + 1).min(state.agents.len().saturating_sub(1));
                    }
                }
                // Navigate up
                (KeyCode::Char('k'), _) | (KeyCode::Up, _) => {
                    state.agent_selection = state.agent_selection.saturating_sub(1);
                }
                // Select agent
                (KeyCode::Enter, _) => {
                    if !state.agents.is_empty() && state.agent_selection < state.agents.len() {
                        let selected = state.agents[state.agent_selection].0.clone();
                        state.mode = AppMode::Normal;
                        return Some(selected);
                    }
                    state.mode = AppMode::Normal;
                }
                // Close picker
                (KeyCode::Esc, _) | (KeyCode::Char('q'), _) => {
                    state.mode = AppMode::Normal;
                }
                _ => {}
            }
        }
        AppMode::ContextManager => {
            *last_key_was_g = false;
            if state.show_input_dialog {
                match key.code {
                    KeyCode::Enter => {
                        let path = state.dialog_input.trim().to_string();
                        if !path.is_empty() {
                            state.context_files.push(path);
                            let req = serde_json::json!({
                                "type": "context_update",
                                "files": state.context_files,
                                "group": state.group
                            });
                            let _ = ws_out_tx.send(req.to_string()).await;
                        }
                        state.dialog_input.clear();
                        state.show_input_dialog = false;
                    }
                    KeyCode::Esc => {
                        state.show_input_dialog = false;
                    }
                    KeyCode::Backspace => {
                        state.dialog_input.pop();
                    }
                    KeyCode::Char(c) => {
                        state.dialog_input.push(c);
                    }
                    _ => {}
                }
                return None;
            }
            match key.code {
                KeyCode::Esc | KeyCode::F(4) => {
                    state.mode = AppMode::Normal;
                }
                KeyCode::Char('a') => {
                    state.show_input_dialog = true;
                    state.dialog_input.clear();
                }
                KeyCode::Char('d') => {
                    if !state.context_files.is_empty() && state.context_selection < state.context_files.len() {
                        state.context_files.remove(state.context_selection);
                        state.context_selection = state.context_selection.saturating_sub(1);
                        let req = serde_json::json!({
                            "type": "context_update",
                            "files": state.context_files,
                            "group": state.group
                        });
                        let _ = ws_out_tx.send(req.to_string()).await;
                    }
                }
                KeyCode::Char('c') => {
                    state.context_files.clear();
                    state.context_selection = 0;
                    let req = serde_json::json!({
                        "type": "context_update",
                        "files": state.context_files,
                        "group": state.group
                    });
                    let _ = ws_out_tx.send(req.to_string()).await;
                }
                KeyCode::Up | KeyCode::Char('k') => {
                    state.context_selection = state.context_selection.saturating_sub(1);
                }
                KeyCode::Down | KeyCode::Char('j') => {
                    if !state.context_files.is_empty() {
                        state.context_selection = (state.context_selection + 1).min(state.context_files.len() - 1);
                    }
                }
                _ => {}
            }
        }
        AppMode::BrainstormWizard => {
            *last_key_was_g = false;
            match key.code {
                KeyCode::Esc => {
                    state.mode = AppMode::Normal;
                }
                KeyCode::Enter => {
                    if state.brainstorm_step < 3 {
                        state.brainstorm_step += 1;
                    } else {
                        let proposal = format!(
                            "## Why\n\n{}\n\n## Approaches\n\n### Approach 1\n{}\n\n### Approach 2\n{}\n\n## Trade-offs\n\n{}",
                            state.brainstorm_answers[0],
                            state.brainstorm_answers[1],
                            state.brainstorm_answers[2],
                            state.brainstorm_answers[3]
                        );
                        state.input = proposal;
                        state.cursor_position = state.input.chars().count();
                        state.mode = AppMode::Normal;
                    }
                }
                KeyCode::Backspace => {
                    state.brainstorm_answers[state.brainstorm_step].pop();
                }
                KeyCode::Char(c) => {
                    state.brainstorm_answers[state.brainstorm_step].push(c);
                }
                _ => {}
            }
        }
        AppMode::GitDashboard => {
            *last_key_was_g = false;
            match key.code {
                KeyCode::Esc | KeyCode::F(7) => {
                    state.mode = AppMode::Normal;
                }
                KeyCode::Up | KeyCode::Char('k') => {
                    state.git_selection = state.git_selection.saturating_sub(1);
                }
                KeyCode::Down | KeyCode::Char('j') => {
                    if !state.git_files.is_empty() {
                        state.git_selection = (state.git_selection + 1).min(state.git_files.len() - 1);
                    }
                }
                KeyCode::Char(' ') => {
                    if !state.git_files.is_empty() && state.git_selection < state.git_files.len() {
                        let (ref file, ref current_status) = state.git_files[state.git_selection];
                        let target_staged = current_status != "Staged";
                        let req = serde_json::json!({
                            "type": "stage_file",
                            "file": file.clone(),
                            "stage": target_staged,
                            "group": state.group
                        });
                        let _ = ws_out_tx.send(req.to_string()).await;
                    }
                }
                _ => {}
            }
        }
    }
    None
}

fn handle_mouse(state: &mut AppState, mouse: crossterm::event::MouseEvent) {
    match mouse.kind {
        MouseEventKind::ScrollUp => {
            state.scroll_offset += 3;
        }
        MouseEventKind::ScrollDown => {
            state.scroll_offset = state.scroll_offset.saturating_sub(3);
        }
        _ => {}
    }
}
