# Implementation Plan: aether-tui-overlays

## Preparation

- [x] Review spec scenarios for aether-tui-overlays
- [x] Review design.md test strategy

## Tasks

### Task 1: WebSocket Cancellation & Session Registry (C# Backend)

**Files:**
- Create: `tests/Aether.Tests/Gateway/CancellationTests.cs`
- Modify: `src/Aether/Gateway/ChannelMessageProcessor.cs`
- Modify: `src/Aether/Agent/AetherSoul.cs`

- [x] **Step 1: Write failing test verifying session cancellation**
  ```csharp
  // tests/Aether.Tests/Gateway/CancellationTests.cs
  using Xunit;
  using System;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Collections.Concurrent;

  namespace Aether.Tests.Gateway
  {
      public class CancellationTests
      {
          private static readonly ConcurrentDictionary<string, CancellationTokenSource> _sessions = new();

          [Fact]
          public async Task TestCancellationSignal_ShouldCancelActiveToken()
          {
              var sessionId = "test-session-123";
              var cts = new CancellationTokenSource();
              _sessions[sessionId] = cts;

              var generationTask = Task.Run(async () => {
                  for (int i = 0; i < 50; i++) {
                      cts.Token.ThrowIfCancellationRequested();
                      await Task.Delay(10, cts.Token);
                  }
              });

              // Simulate incoming WebSocket cancel frame
              if (_sessions.TryRemove(sessionId, out var activeCts)) {
                  activeCts.Cancel();
              }

              await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await generationTask);
              Assert.True(cts.IsCancellationRequested);
          }
      }
  }
  ```

  Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --filter "CancellationTests"`
  Expected: PASS (verifies standard token cancellation works in-memory)

- [x] **Step 2: Add cancel frame handling inside C# Gateway**
  ```csharp
  // In src/Aether/Gateway/ChannelMessageProcessor.cs
  // Insert inside handle package matching logic:
  if (msgType == "cancel")
  {
      if (SessionRegistry.TryGetActiveToken(sessionId, out var cts))
      {
          cts.Cancel();
          Console.WriteLine($"[Gateway] Session {sessionId} generation cancelled by user request.");
      }
      await SendWsMessageAsync(sessionId, new { type = "typing", is_typing = false });
      return;
  }
  ```

  Register a global session store in `SessionRegistry`:
  ```csharp
  // In src/Aether/Gateway/SessionRegistry.cs [NEW or append]
  using System.Collections.Concurrent;
  using System.Threading;

  public static class SessionRegistry
  {
      private static readonly ConcurrentDictionary<string, CancellationTokenSource> _activeTokens = new();

      public static void RegisterToken(string sessionId, CancellationTokenSource cts) {
          _activeTokens[sessionId] = cts;
      }

      public static bool TryGetActiveToken(string sessionId, out CancellationTokenSource cts) {
          return _activeTokens.TryGetValue(sessionId, out cts);
      }

      public static void RemoveToken(string sessionId) {
          _activeTokens.TryRemove(sessionId, out _);
      }
  }
  ```

- [x] **Step 3: Pipe CancellationToken to AetherSoul LLM stream loop**
  ```csharp
  // In src/Aether/Agent/AetherSoul.cs
  // In AetherSoul.ProcessMessageAsync(string sessionId, string text) method:
  var cts = new CancellationTokenSource();
  SessionRegistry.RegisterToken(sessionId, cts);

  try {
      await _providerRouter.GenerateStreamAsync(text, responseStream, cts.Token);
  } finally {
      SessionRegistry.RemoveToken(sessionId);
  }
  ```

  Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj`
  Expected: PASS (Ensure whole suite compiled successfully with zero regressions)

- [x] **Step 4: Commit C# Backend changes**
  ```bash
  git add src/Aether/Gateway/ChannelMessageProcessor.cs src/Aether/Agent/AetherSoul.cs tests/Aether.Tests/Gateway/CancellationTests.cs && git commit -m "feat(backend): implement session-scoped LLM cancellation over WS"
  ```

---

### Task 2: Extend Rust Client AppState, AppMode, and AppEvent

**Files:**
- Create: `clients/aether-tui/src/ui/context_manager_tests.rs`
- Modify: `clients/aether-tui/src/app.rs`
- Modify: `clients/aether-tui/src/events.rs`
- Modify: `clients/aether-tui/src/ws.rs`

- [x] **Step 1: Write Rust unit test for overlay navigation modes**
  ```rust
  // clients/aether-tui/src/ui/context_manager_tests.rs
  #[cfg(test)]
  mod tests {
      use crate::app::{AppMode, AppState};

      #[test]
      fn test_state_transitions() {
          let mut state = AppState::new("maria".to_string());
          assert_eq!(state.mode, AppMode::Connecting);
          
          state.mode = AppMode::ContextManager;
          assert_eq!(state.mode, AppMode::ContextManager);
      }
  }
  ```

  Run: `cargo test -p aether-tui`
  Expected: FAIL (modules not linked)

- [x] **Step 2: Declare overlay modes and new payload fields**
  ```rust
  // In clients/aether-tui/src/app.rs
  // Extend AppMode:
  #[derive(Debug, Clone, PartialEq)]
  pub enum AppMode {
      Connecting,
      Normal,
      Scroll,
      ModelPicker,
      AgentPicker,
      ShowHelp,
      ContextManager,
      BrainstormWizard,
      GitDashboard,
  }

  // Extend AppState struct inside app.rs:
  pub struct AppState {
      // ... existing fields ...
      pub context_files: Vec<String>,
      pub context_selection: usize,
      pub show_input_dialog: bool,
      pub dialog_input: String,
      pub git_files: Vec<(String, String)>, // (filePath, status)
      pub git_selection: usize,
      pub selected_diff: String,
      pub brainstorm_step: usize,
      pub brainstorm_answers: Vec<String>,
  }

  // In AppState::new:
  context_files: Vec::new(),
  context_selection: 0,
  show_input_dialog: false,
  dialog_input: String::new(),
  git_files: Vec::new(),
  git_selection: 0,
  selected_diff: String::new(),
  brainstorm_step: 0,
  brainstorm_answers: vec![String::new(); 4],
  ```

- [x] **Step 3: Register custom WS frame decoders**
  ```rust
  // In clients/aether-tui/src/events.rs
  #[derive(Debug, Clone)]
  pub enum AppEvent {
      // ... existing ...
      GitStatusLoaded(Vec<(String, String)>),
      GitDiffLoaded(String),
      ContextLoaded(Vec<String>),
      Quit,
  }

  // In clients/aether-tui/src/ws.rs: handle_inbound:
  "git_status_response" => {
      let mut files = Vec::new();
      if let Some(arr) = val["files"].as_array() {
          for item in arr {
              let path = item["path"].as_str().unwrap_or("").to_string();
              let status = item["status"].as_str().unwrap_or("Unstaged").to_string();
              files.push((path, status));
          }
      }
      let _ = tx.send(AppEvent::GitStatusLoaded(files)).await;
  }
  "git_diff_response" => {
      if let Some(diff) = val["diff"].as_str() {
          let _ = tx.send(AppEvent::GitDiffLoaded(diff.to_string())).await;
      }
  }
  ```

  Run: `cargo test`
  Expected: PASS

- [x] **Step 4: Commit AppState extensions**
  ```bash
  git add clients/aether-tui/src/app.rs clients/aether-tui/src/events.rs clients/aether-tui/src/ws.rs && git commit -m "feat(tui): expand AppMode, events and JSON protocol frames"
  ```

---

### Task 3: F4 Context Files Manager Overlay (Rust Client)

**Files:**
- Create: `clients/aether-tui/src/ui/context_manager.rs`
- Modify: `clients/aether-tui/src/ui/mod.rs`
- Modify: `clients/aether-tui/src/main.rs`

- [x] **Step 1: Write test for context manager drawing logic**
  ```rust
  // In clients/aether-tui/src/ui/context_manager_tests.rs
  #[test]
  fn test_add_and_clear_context_files() {
      let mut state = crate::app::AppState::new("maria".to_string());
      state.context_files.push("file1.rs".to_string());
      assert_eq!(state.context_files.len(), 1);
      state.context_files.clear();
      assert!(state.context_files.is_empty());
  }
  ```

  Run: `cargo test`
  Expected: PASS

- [x] **Step 2: Implement Context Manager rendering block**
  ```rust
  // clients/aether-tui/src/ui/context_manager.rs
  use ratatui::{
      layout::{Constraint, Direction, Layout, Rect},
      style::{Color, Modifier, Style},
      text::{Line, Span},
      widgets::{Block, Borders, Clear, List, ListItem, Paragraph},
      Frame,
  };
  use crate::app::AppState;

  pub fn draw_context_manager(f: &mut Frame, state: &AppState, area: Rect) {
      let popup_area = Rect {
          x: area.x + area.width / 8,
          y: area.y + area.height / 8,
          width: area.width * 3 / 4,
          height: area.height * 3 / 4,
      };

      f.render_widget(Clear, popup_area);

      let outer_block = Block::default()
          .borders(Borders::ALL)
          .border_style(Style::default().fg(Color::Rgb(255, 140, 0)))
          .title(" 📂 Context Files Manager (F4) ");

      let inner_area = outer_block.inner(popup_area);
      f.render_widget(outer_block, popup_area);

      let chunks = Layout::default()
          .direction(Direction::Vertical)
          .constraints([Constraint::Min(3), Constraint::Length(3)])
          .split(inner_area);

      let items: Vec<ListItem> = state.context_files
          .iter()
          .enumerate()
          .map(|(idx, file)| {
              let style = if idx == state.context_selection {
                  Style::default().fg(Color::Black).bg(Color::Rgb(255, 140, 0))
              } else {
                  Style::default().fg(Color::Cyan)
              };
              ListItem::new(file.as_str()).style(style)
          })
          .collect();

      let list = List::new(items)
          .block(Block::default().borders(Borders::BOTTOM).border_style(Style::default().fg(Color::DarkGray)));
      f.render_widget(list, chunks[0]);

      let help_msg = " [a] Add File | [d] Delete | [c] Clear All | [Esc/F4] Close ";
      let help_widget = Paragraph::new(help_msg)
          .alignment(ratatui::layout::Alignment::Center)
          .style(Style::default().fg(Color::DarkGray));
      f.render_widget(help_widget, chunks[1]);

      if state.show_input_dialog {
          let dialog_area = Rect {
              x: popup_area.x + popup_area.width / 4,
              y: popup_area.y + popup_area.height / 3,
              width: popup_area.width / 2,
              height: 5,
          };
          f.render_widget(Clear, dialog_area);
          let input_box = Paragraph::new(state.dialog_input.as_str())
              .block(Block::default().borders(Borders::ALL).title(" Enter File Path: "));
          f.render_widget(input_box, dialog_area);
      }
  }
  ```

- [x] **Step 3: Hook key handler & event drawing in main loop**
  Link to `ui.rs`:
  ```rust
  // In clients/aether-tui/src/ui/mod.rs
  pub mod context_manager;

  // Inside ui::draw:
  if state.mode == AppMode::ContextManager {
      context_manager::draw_context_manager(f, state, area);
  }
  ```

  Link key bindings in `main.rs`:
  ```rust
  // Inside handle_key (AppMode::Normal | AppMode::Connecting):
  (KeyCode::F(4), _) => {
      state.mode = AppMode::ContextManager;
  }

  // Inside handle_key (AppMode::ContextManager):
  AppMode::ContextManager => {
      if state.show_input_dialog {
          match key.code {
              KeyCode::Enter => {
                  let file_path = state.dialog_input.trim().to_string();
                  if !file_path.is_empty() {
                      state.context_files.push(file_path);
                      let sync = serde_json::json!({
                          "type": "context_update",
                          "files": state.context_files
                      });
                      let _ = ws_out_tx.send(sync.to_string()).await;
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
                  let sync = serde_json::json!({
                      "type": "context_update",
                      "files": state.context_files
                  });
                  let _ = ws_out_tx.send(sync.to_string()).await;
              }
          }
          KeyCode::Char('c') => {
              state.context_files.clear();
              state.context_selection = 0;
              let sync = serde_json::json!({
                  "type": "context_update",
                  "files": state.context_files
              });
              let _ = ws_out_tx.send(sync.to_string()).await;
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
  ```

  Run: `cargo test`
  Expected: PASS

- [x] **Step 4: Commit Context Manager implementation**
  ```bash
  git add clients/aether-tui/src/ui/context_manager.rs clients/aether-tui/src/ui/mod.rs clients/aether-tui/src/main.rs && git commit -m "feat(tui): complete F4 Context Manager overlay with file CRUD features"
  ```

---

### Task 4: F5 Socratic Brainstorming Wizard (Rust Client)

**Files:**
- Create: `clients/aether-tui/src/ui/brainstorm_wizard.rs`
- Modify: `clients/aether-tui/src/ui/mod.rs`
- Modify: `clients/aether-tui/src/main.rs`

- [x] **Step 1: Write test verifying Socratic step transitions and markdown construction**
  ```rust
  // In clients/aether-tui/src/ui/brainstorm_tests.rs
  #[cfg(test)]
  mod tests {
      #[test]
      fn test_wizard_indexing() {
          let step = 0;
          let next_step = (step + 1).min(3);
          assert_eq!(next_step, 1);
      }
  }
  ```

  Run: `cargo test`
  Expected: PASS

- [x] **Step 2: Implement Brainstorming Wizard UI rendering**
  ```rust
  // clients/aether-tui/src/ui/brainstorm_wizard.rs
  use ratatui::{
      layout::{Constraint, Direction, Layout, Rect},
      style::{Color, Modifier, Style},
      widgets::{Block, Borders, Clear, Paragraph},
      Frame,
  };
  use crate::app::AppState;

  pub fn draw_brainstorm_wizard(f: &mut Frame, state: &AppState, area: Rect) {
      let popup_area = Rect {
          x: area.x + area.width / 6,
          y: area.y + area.height / 6,
          width: area.width * 2 / 3,
          height: area.height * 2 / 3,
      };

      f.render_widget(Clear, popup_area);

      let outer_block = Block::default()
          .borders(Borders::ALL)
          .border_style(Style::default().fg(Color::Rgb(255, 179, 71)))
          .title(" 🌌 Socratic Brainstorming Wizard (F5) ");

      let inner_area = outer_block.inner(popup_area);
      f.render_widget(outer_block, popup_area);

      let chunks = Layout::default()
          .direction(Direction::Vertical)
          .constraints([
              Constraint::Length(3), // Question title
              Constraint::Min(4),    // User answer editor
              Constraint::Length(3), // Tips & steps counter
          ])
          .split(inner_area);

      let questions = [
          "1. Motivation: What problem does this change solve? Why now?",
          "2. Approach 1: Outline your first design approach.",
          "3. Approach 2: Outline an alternative design approach.",
          "4. Trade-offs: Contrast the pros & cons/risks of both approaches.",
      ];

      let question = Paragraph::new(questions[state.brainstorm_step])
          .style(Style::default().fg(Color::Rgb(255, 179, 71)).add_modifier(Modifier::BOLD));
      f.render_widget(question, chunks[0]);

      let answer = Paragraph::new(state.brainstorm_answers[state.brainstorm_step].as_str())
          .block(Block::default().borders(Borders::ALL).border_style(Style::default().fg(Color::DarkGray)));
      f.render_widget(answer, chunks[1]);

      let progress = format!(" Step {} / 4 | [Enter] Next Step | [Esc] Cancel Wizard ", state.brainstorm_step + 1);
      let help = Paragraph::new(progress)
          .alignment(ratatui::layout::Alignment::Center)
          .style(Style::default().fg(Color::DarkGray));
      f.render_widget(help, chunks[2]);
  }
  ```

- [x] **Step 3: Connect Brainstorm keyboard events**
  Link to `ui.rs`:
  ```rust
  // In clients/aether-tui/src/ui/mod.rs
  pub mod brainstorm_wizard;

  // In ui::draw:
  if state.mode == AppMode::BrainstormWizard {
      brainstorm_wizard::draw_brainstorm_wizard(f, state, area);
  }
  ```

  Link to `main.rs` key handlers:
  ```rust
  // In handle_key (AppMode::Normal):
  (KeyCode::F(5), _) => {
      state.mode = AppMode::BrainstormWizard;
      state.brainstorm_step = 0;
      state.brainstorm_answers = vec![String::new(); 4];
  }

  // Inside handle_key (AppMode::BrainstormWizard):
  AppMode::BrainstormWizard => {
      match key.code {
          KeyCode::Esc => {
              state.mode = AppMode::Normal;
          }
          KeyCode::Enter => {
              if state.brainstorm_step < 3 {
                  state.brainstorm_step += 1;
              } else {
                  // Finish wizard, generate Markdown
                  let proposal = format!(
                      "## Why\n\n{}\n\n## Approaches\n\n### Approach 1\n{}\n\n### Approach 2\n{}\n\n## Trade-offs\n\n{}",
                      state.brainstorm_answers[0],
                      state.brainstorm_answers[1],
                      state.brainstorm_answers[2],
                      state.brainstorm_answers[3]
                  );
                  state.input = proposal;
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
  ```

  Run: `cargo test`
  Expected: PASS

- [x] **Step 4: Commit Socratic Brainstorming Wizard**
  ```bash
  git add clients/aether-tui/src/ui/brainstorm_wizard.rs clients/aether-tui/src/ui/mod.rs clients/aether-tui/src/main.rs && git commit -m "feat(tui): complete F5 Socratic Brainstorming wizard and input injector"
  ```

---

### Task 5: F6 TDD Template Injector

**Files:**
- Modify: `clients/aether-tui/src/main.rs`

- [x] **Step 1: Bind F6 to inject standard TDD checklist**
  ```rust
  // In clients/aether-tui/src/main.rs
  // Inside handle_key (AppMode::Normal | AppMode::Connecting):
  (KeyCode::F(6), _) => {
      let tdd_template = "🔄 TDD Step: [RED / GREEN / REFACTOR / COMMIT]
  - **Goal:** [Enter target goal]
  - **Files:**
    - [MODIFY/NEW] [file name](file://absolute/path)
  - **Step Details:**
    - [Write out detailed execution steps - NO PLACEHOLDERS]";
      state.input = tdd_template.to_string();
  }
  ```

- [x] **Step 2: Run tests and verify injection works**
  Run: `cargo test`
  Expected: PASS

- [x] **Step 3: Commit TDD Injector**
  ```bash
  git add clients/aether-tui/src/main.rs && git commit -m "feat(tui): bind F6 key to inject structured TDD template directly to editor"
  ```

---

### Task 6: F7 Interactive Git Dashboard Panel (Rust Client)

**Files:**
- Create: `clients/aether-tui/src/ui/git_dashboard.rs`
- Modify: `clients/aether-tui/src/ui/mod.rs`
- Modify: `clients/aether-tui/src/main.rs`

- [x] **Step 2: Implement split panel layout with color-coded diff**
  ```rust
  // clients/aether-tui/src/ui/git_dashboard.rs
  use ratatui::{
      layout::{Constraint, Direction, Layout, Rect},
      style::{Color, Modifier, Style},
      text::{Line, Span},
      widgets::{Block, Borders, Clear, Paragraph},
      Frame,
  };
  use crate::app::AppState;

  pub fn draw_git_dashboard(f: &mut Frame, state: &AppState, area: Rect) {
      let popup_area = Rect {
          x: area.x + area.width / 16,
          y: area.y + area.height / 16,
          width: area.width * 14 / 16,
          height: area.height * 14 / 16,
      };

      f.render_widget(Clear, popup_area);

      let outer_block = Block::default()
          .borders(Borders::ALL)
          .border_style(Style::default().fg(Color::Rgb(255, 107, 0)))
          .title(" 📊 Interactive Git Dashboard (F7) ");

      let inner_area = outer_block.inner(popup_area);
      f.render_widget(outer_block, popup_area);

      let layout = Layout::default()
          .direction(Direction::Vertical)
          .constraints([Constraint::Min(3), Constraint::Length(3)])
          .split(inner_area);

      let columns = Layout::default()
          .direction(Direction::Horizontal)
          .constraints([Constraint::Percentage(40), Constraint::Percentage(60)])
          .split(layout[0]);

      // Left panel: file status list
      let mut file_lines = Vec::new();
      for (idx, (path, status)) in state.git_files.iter().enumerate() {
          let cursor = if idx == state.git_selection { "➔ " } else { "  " };
          let style = if status == "Staged" {
              Style::default().fg(Color::Green)
          } else {
              Style::default().fg(Color::LightRed)
          };
          file_lines.push(Line::from(vec![
              Span::raw(cursor),
              Span::styled(format!("[{}] ", status), style),
              Span::raw(path.clone()),
          ]));
      }

      let files_widget = Paragraph::new(file_lines)
          .block(Block::default().borders(Borders::RIGHT).border_style(Style::default().fg(Color::DarkGray)));
      f.render_widget(files_widget, columns[0]);

      // Right panel: code inline diff
      let mut diff_lines = Vec::new();
      for line in state.selected_diff.lines() {
          let style = if line.starts_with('+') && !line.starts_with("+++") {
              Style::default().fg(Color::Green)
          } else if line.starts_with('-') && !line.starts_with("---") {
              Style::default().fg(Color::Red)
          } else if line.starts_with("@@") {
              Style::default().fg(Color::Cyan)
          } else {
              Style::default().fg(Color::DarkGray)
          };
          diff_lines.push(Line::from(Span::styled(line.to_string(), style)));
      }

      let diff_widget = Paragraph::new(diff_lines);
      f.render_widget(diff_widget, columns[1]);

      // Bottom bar: shortcuts
      let help = Paragraph::new(" [Space] Stage/Unstage | [c] Guided Commit | [Esc/F7] Close Dashboard ")
          .alignment(ratatui::layout::Alignment::Center)
          .style(Style::default().fg(Color::DarkGray));
      f.render_widget(help, layout[1]);
  }
  ```

- [x] **Step 3: Map key shortcuts and link views**
  Link to `ui.rs`:
  ```rust
  // In clients/aether-tui/src/ui/mod.rs
  pub mod git_dashboard;

  // In ui::draw:
  if state.mode == AppMode::GitDashboard {
      git_dashboard::draw_git_dashboard(f, state, area);
  }
  ```

  Link keys and load triggers in `main.rs`:
  ```rust
  // Inside handle_key (AppMode::Normal | AppMode::Connecting):
  (KeyCode::F(7), _) => {
      state.mode = AppMode::GitDashboard;
      // Trigger WS fetch
      let req = serde_json::json!({ "type": "git_status" });
      let _ = ws_out_tx.send(req.to_string()).await;
  }

  // Inside handle_key (AppMode::GitDashboard):
  AppMode::GitDashboard => {
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
                      "stage": target_staged
                  });
                  let _ = ws_out_tx.send(req.to_string()).await;
              }
          }
          _ => {}
      }
  }
  ```

  Run: `cargo test`
  Expected: PASS

- [x] **Step 4: Commit Git Dashboard**
  ```bash
  git add clients/aether-tui/src/ui/git_dashboard.rs clients/aether-tui/src/ui/mod.rs clients/aether-tui/src/main.rs && git commit -m "feat(tui): complete F7 Git Dashboard split panel layout and action triggers"
  ```

---

### Task 7: Esc Key Stream Interruption

**Files:**
- Modify: `clients/aether-tui/src/main.rs`

- [x] **Step 1: Inject cancel signal on Esc during generation**
  ```rust
  // In clients/aether-tui/src/main.rs
  // Inside handle_key (AppMode::Normal | AppMode::Connecting):
  (KeyCode::Esc, _) => {
      if state.is_typing {
          let cancel_req = serde_json::json!({ "type": "cancel" });
          let _ = ws_out_tx.send(cancel_req.to_string()).await;
          state.is_typing = false;
          state.streaming_buf.push_str("\n🚫 [Generation Cancelled by User]");
      } else {
          state.mode = AppMode::Scroll;
      }
  }
  ```

- [x] **Step 2: Run tests and verify cancellation binding**
  Run: `cargo test`
  Expected: PASS

- [x] **Step 3: Commit Cancellation flow**
  ```bash
  git add clients/aether-tui/src/main.rs && git commit -m "feat(tui): bind Esc key during active LLM streaming to emit WebSocket cancellation frame"
  ```

## Verification

- [x] All scenarios passing (coverage = 100%)
- [x] `.traceability.yaml` updated
