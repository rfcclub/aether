# Aether TUI Modern Minimalist UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade Aether TUI client styling to a modern minimalist design using rounded borders and an Aura x Athanor hybrid color palette (deep black background, violet/pink active focus, and soft violet/amber accents).

**Architecture:** Update UI color constants, layout margins, border types, status bar, and code syntax highlighting in `ui.rs` and the submodule popups.

**Tech Stack:** Rust, Ratatui, Crossterm.

---

### Task 1: Update Theme Colors and Imports in `ui.rs`

**Files:**
- Modify: `clients/aether-tui/src/ui.rs:1-31`
- Test: Build project using `cargo check`

- [ ] **Step 1: Update imports to include BorderType**
  
  Add `BorderType` to the `use ratatui::widgets::{...}` block.
  
  ```rust
  use ratatui::{
      Frame,
      layout::{Alignment, Constraint, Direction, Layout, Rect},
      style::{Color, Modifier, Style},
      text::{Line, Span, Text},
      widgets::{Block, BorderType, Borders, Clear, List, ListItem, Paragraph, Wrap},
  };
  ```

- [ ] **Step 2: Update Athanor Fire Theme Color Constants**
  
  Change the color codes in `clients/aether-tui/src/ui.rs` to match the Aura × Athanor hybrid palette:
  
  ```rust
  // ── Athanor Fire Theme ────────────────────────────────────────────────────────
  pub const BG:           Color = Color::Rgb(8,   8,   8);    // Deep Black
  pub const USER_NAME:    Color = Color::Rgb(91,  200, 245);  // Ice Blue
  pub const USER_TEXT:    Color = Color::Rgb(220, 220, 220);  // Soft White-Gray
  pub const AGENT_NAME:   Color = Color::Rgb(236, 72,  153);  // Aura Pink
  pub const AGENT_TEXT:   Color = Color::Rgb(243, 232, 255);  // Soft Violet-White
  pub const CURSOR_COL:   Color = Color::Rgb(236, 72,  153);  // Aura Pink
  pub const BORDER_FOCUS: Color = Color::Rgb(168, 85,  247);  // Aura Violet active border
  pub const CONNECTED:    Color = Color::Rgb(68,  255, 136);
  pub const DISCONNECTED: Color = Color::Rgb(80,  80,  80);   // Darker Gray
  pub const ERROR_COL:    Color = Color::Rgb(255, 80,  80);
  pub const DIM:          Color = Color::Rgb(120, 110, 130);  // Muted Violet-Gray
  pub const AMBER:        Color = Color::Rgb(255, 140, 0);    // Athanor Orange
  pub const VIOLET:       Color = Color::Rgb(168, 85,  247);  // Aura Violet
  pub const PICKER_SEL:   Color = Color::Rgb(168, 85,  247);
  pub const PICKER_HDR:   Color = Color::Rgb(180, 180, 180);
  ```

- [ ] **Step 3: Verify build check succeeds**
  
  Run: `cargo check --manifest-path clients/aether-tui/Cargo.toml`
  Expected output: Compilation success (warnings about unused/changed variables are fine).

- [ ] **Step 4: Commit**
  
  ```bash
  git add clients/aether-tui/src/ui.rs
  git commit -m "style: update TUI color palette to Aura x Athanor"
  ```

---

### Task 2: Apply Rounded Borders to Main UI Elements and Popups in `ui.rs`

**Files:**
- Modify: `clients/aether-tui/src/ui.rs`
- Test: Build project using `cargo check`

- [ ] **Step 1: Set Rounded Borders on Input Bar**
  
  Update `draw_input` to apply `.border_type(BorderType::Rounded)`.
  
  ```rust
  let input_block = Block::default()
      .borders(Borders::ALL)
      .border_type(BorderType::Rounded)
      .border_style(Style::default().fg(border_color))
      .style(Style::default().bg(BG));
  ```

- [ ] **Step 2: Set Rounded Borders on Model Picker popup**
  
  Update `draw_model_picker` to use rounded borders.
  
  ```rust
  let block = Block::default()
      .borders(Borders::ALL)
      .border_type(BorderType::Rounded)
      .border_style(Style::default().fg(BORDER_FOCUS))
      .style(Style::default().bg(BG))
      .title(Span::styled(" Models ", Style::default().fg(AGENT_NAME).add_modifier(Modifier::BOLD)))
      .title_bottom(Span::styled(
          " j/k Navigate · Enter Select · Esc Close ",
          Style::default().fg(DIM),
      ));
  ```

- [ ] **Step 3: Set Rounded Borders on Agent Picker popup**
  
  Update `draw_agent_picker` to use rounded borders.
  
  ```rust
  let block = Block::default()
      .borders(Borders::ALL)
      .border_type(BorderType::Rounded)
      .border_style(Style::default().fg(BORDER_FOCUS))
      .style(Style::default().bg(BG))
      .title(Span::styled(" Choose Agent ", Style::default().fg(AGENT_NAME).add_modifier(Modifier::BOLD)))
      .title_bottom(Span::styled(
          " j/k Navigate · Enter Select · Esc Close ",
          Style::default().fg(DIM),
      ));
  ```

- [ ] **Step 4: Set Rounded Borders on Help popup**
  
  Update `draw_help_popup` to use rounded borders.
  
  ```rust
  let help_para = Paragraph::new(Text::from(help_text))
      .block(
          Block::default()
              .borders(Borders::ALL)
              .border_type(BorderType::Rounded)
              .border_style(Style::default().fg(BORDER_FOCUS))
              .style(Style::default().bg(BG))
              .title(Span::styled(" Help ", Style::default().fg(AGENT_NAME).add_modifier(Modifier::BOLD))),
      )
      .style(Style::default().bg(BG));
  ```

- [ ] **Step 5: Verify build check**
  
  Run: `cargo check --manifest-path clients/aether-tui/Cargo.toml`
  Expected: Success.

- [ ] **Step 6: Commit**
  
  ```bash
  git add clients/aether-tui/src/ui.rs
  git commit -m "style: apply rounded borders to main elements and popups in ui.rs"
  ```

---

### Task 3: Refine Header, Chat Quotes, and Status Bar Layout in `ui.rs`

**Files:**
- Modify: `clients/aether-tui/src/ui.rs`
- Test: Build project using `cargo check`

- [ ] **Step 1: Refine quote lines in chat rendering**
  
  Update `draw_chat` quote border prefix from `│ ` to Aura's custom color. Find where quotes are processed:
  
  ```rust
  } else if is_quote {
      spans.push(Span::styled("│ ", Style::default().fg(VIOLET)));
  }
  ```

- [ ] **Step 2: Clean and structure the Status Bar (Footer)**
  
  Update `draw_status` to use elegant `│` delimiters and format options clearly.
  
  ```rust
  let mut spans = vec![
      Span::styled(format!(" {} ", conn_dot), Style::default().fg(conn_color)),
      Span::styled(format!("{} │ ", group_cap), Style::default().fg(DIM)),
      Span::styled(mode_text, mode_style),
      Span::styled(" │ ", Style::default().fg(DIM)),
      Span::styled("[F1] Help", Style::default().fg(DIM)),
      Span::styled(" │ ", Style::default().fg(DIM)),
      Span::styled("[Ctrl+Q] Quit", Style::default().fg(DIM)),
      Span::styled(" │ ", Style::default().fg(DIM)),
      Span::styled("[Esc] Scroll", Style::default().fg(DIM)),
      Span::styled(" │ ", Style::default().fg(DIM)),
      Span::styled("[F2] Models", Style::default().fg(DIM)),
      Span::styled(" │ ", Style::default().fg(DIM)),
      Span::styled("[F3] Agents", Style::default().fg(DIM)),
      Span::styled(" │ ", Style::default().fg(DIM)),
      Span::styled("[F4] Context", Style::default().fg(DIM)),
      Span::styled(" │ ", Style::default().fg(DIM)),
      Span::styled("[F5] Brainstorm", Style::default().fg(DIM)),
      Span::styled(" │ ", Style::default().fg(DIM)),
      Span::styled("[F6] TDD", Style::default().fg(DIM)),
      Span::styled(" │ ", Style::default().fg(DIM)),
      Span::styled("[F7] Git", Style::default().fg(DIM)),
  ];
  ```

- [ ] **Step 3: Verify build**
  
  Run: `cargo check --manifest-path clients/aether-tui/Cargo.toml`
  Expected: Success.

- [ ] **Step 4: Commit**
  
  ```bash
  git add clients/aether-tui/src/ui.rs
  git commit -m "style: refine chat quote lines and status bar layout"
  ```

---

### Task 4: Expand Code Syntax Highlighting in `ui.rs`

**Files:**
- Modify: `clients/aether-tui/src/ui.rs`
- Test: Build project using `cargo check`

- [ ] **Step 1: Enhance syntax highlighting to support strings and numbers**
  
  Modify the `highlight_code_line` function to detect string literals (`"..."`), numbers, and more key terms.
  
  Replace the body of `highlight_code_line` around line 874:
  
  ```rust
  fn highlight_code_line(text: &str) -> Line<'static> {
      let mut spans = vec![];
      let words: Vec<&str> = text.split_inclusive(|c: char| !c.is_alphanumeric() && c != '_').collect();
  
      let keyword_style = Style::default().fg(Color::Rgb(168, 85, 247)).add_modifier(Modifier::BOLD); // Violet
      let type_style = Style::default().fg(Color::Rgb(91, 200, 245)); // Ice Blue
      let string_style = Style::default().fg(Color::Rgb(100, 220, 120)); // Soft Green
      let number_style = Style::default().fg(Color::Rgb(255, 180, 50)); // Amber
      let comment_style = Style::default().fg(Color::Rgb(100, 100, 100)).add_modifier(Modifier::ITALIC);
      let default_style = Style::default().fg(Color::Rgb(220, 220, 220));
  
      let comment_pos = text.find("//").or_else(|| text.find('#'));
      if let Some(pos) = comment_pos {
          let code_part = &text[..pos];
          let comment_part = &text[pos..];
  
          let mut code_line = highlight_code_line(code_part);
          code_line.spans.push(Span::styled(comment_part.to_string(), comment_style));
          return code_line;
      }
  
      for word in words {
          let trimmed = word.trim_matches(|c: char| !c.is_alphanumeric() && c != '_');
          
          let style = if trimmed.starts_with('"') || word.contains('"') {
              string_style
          } else if trimmed.chars().all(|c| c.is_ascii_digit()) && !trimmed.is_empty() {
              number_style
          } else {
              match trimmed {
                  // Keywords
                  "fn" | "let" | "mut" | "struct" | "enum" | "impl" | "use" | "pub" | "return" | "match" | 
                  "if" | "else" | "loop" | "while" | "for" | "in" | "async" | "await" | "true" | "false" |
                  "using" | "namespace" | "class" | "public" | "private" | "protected" | "internal" | 
                  "static" | "void" | "string" | "int" | "var" | "new" | "get" | "set" => keyword_style,
  
                  // Types
                  "Option" | "Result" | "String" | "Vec" | "Task" | "Console" | "DateTime" | "Ok" | "Err" | "Some" | "None" => type_style,
  
                  _ => default_style,
              }
          };
          spans.push(Span::styled(word.to_string(), style));
      }
  
      Line::from(spans)
  }
  ```

- [ ] **Step 2: Verify build**
  
  Run: `cargo check --manifest-path clients/aether-tui/Cargo.toml`
  Expected: Success.

- [ ] **Step 3: Commit**
  
  ```bash
  git add clients/aether-tui/src/ui.rs
  git commit -m "feat: add string and number syntax highlighting to TUI code blocks"
  ```

---

### Task 5: Apply Rounded Borders to Context Manager Submodule

**Files:**
- Modify: `clients/aether-tui/src/ui/context_manager.rs`
- Test: Build project using `cargo check`

- [ ] **Step 1: Import BorderType in `context_manager.rs`**
  
  Add `BorderType` to imports list.
  
  ```rust
  use ratatui::{
      layout::{Constraint, Direction, Layout, Rect},
      style::{Color, Style},
      widgets::{Block, BorderType, Borders, Clear, List, ListItem, Paragraph},
      Frame,
  };
  ```

- [ ] **Step 2: Set Rounded Borders and Color on Outer and Input Blocks**
  
  Apply `BorderType::Rounded` and Violet active border.
  
  ```rust
  let outer_block = Block::default()
      .borders(Borders::ALL)
      .border_type(BorderType::Rounded)
      .border_style(Style::default().fg(Color::Rgb(168, 85, 247))) // Violet
      .title(" 📂 Context Files Manager (F4) ");
  ```
  
  And for the input dialog box:
  
  ```rust
  let block = Block::default()
      .borders(Borders::ALL)
      .border_type(BorderType::Rounded)
      .border_style(Style::default().fg(Color::Rgb(168, 85, 247)))
      .title(" Enter File Path: ");
  ```

- [ ] **Step 3: Verify build**
  
  Run: `cargo check --manifest-path clients/aether-tui/Cargo.toml`
  Expected: Success.

- [ ] **Step 4: Commit**
  
  ```bash
  git add clients/aether-tui/src/ui/context_manager.rs
  git commit -m "style: apply rounded borders and violet accents to Context Manager"
  ```

---

### Task 6: Apply Rounded Borders to Git Dashboard Submodule

**Files:**
- Modify: `clients/aether-tui/src/ui/git_dashboard.rs`
- Test: Build project using `cargo check`

- [ ] **Step 1: Import BorderType in `git_dashboard.rs`**
  
  Add `BorderType` to imports list.
  
  ```rust
  use ratatui::{
      layout::{Constraint, Direction, Layout, Rect},
      style::{Color, Style},
      text::{Line, Span},
      widgets::{Block, BorderType, Borders, Clear, Paragraph},
      Frame,
  };
  ```

- [ ] **Step 2: Apply Rounded Border on Outer Block**
  
  ```rust
  let outer_block = Block::default()
      .borders(Borders::ALL)
      .border_type(BorderType::Rounded)
      .border_style(Style::default().fg(Color::Rgb(168, 85, 247))) // Violet
      .title(" 📊 Interactive Git Dashboard (F7) ");
  ```

- [ ] **Step 3: Verify build**
  
  Run: `cargo check --manifest-path clients/aether-tui/Cargo.toml`
  Expected: Success.

- [ ] **Step 4: Commit**
  
  ```bash
  git add clients/aether-tui/src/ui/git_dashboard.rs
  git commit -m "style: apply rounded borders and violet accents to Git Dashboard"
  ```

---

### Task 7: Apply Rounded Borders to Brainstorm Wizard Submodule

**Files:**
- Modify: `clients/aether-tui/src/ui/brainstorm_wizard.rs`
- Test: Build project using `cargo check`

- [ ] **Step 1: Import BorderType in `brainstorm_wizard.rs`**
  
  Add `BorderType` to imports list.
  
  ```rust
  use ratatui::{
      layout::{Constraint, Direction, Layout, Rect},
      style::{Color, Modifier, Style},
      widgets::{Block, BorderType, Borders, Clear, Paragraph},
      Frame,
  };
  ```

- [ ] **Step 2: Apply Rounded Border on Outer Block and Answer Box**
  
  Update `outer_block` in `draw_brainstorm_wizard`:
  
  ```rust
  let outer_block = Block::default()
      .borders(Borders::ALL)
      .border_type(BorderType::Rounded)
      .border_style(Style::default().fg(Color::Rgb(168, 85, 247))) // Violet
      .title(" 🌌 Socratic Brainstorming Wizard (F5) ");
  ```
  
  Update `answer_block` in `draw_brainstorm_wizard`:
  
  ```rust
  let answer_block = Block::default()
      .borders(Borders::ALL)
      .border_type(BorderType::Rounded)
      .border_style(Style::default().fg(Color::DarkGray));
  ```

- [ ] **Step 3: Verify build**
  
  Run: `cargo check --manifest-path clients/aether-tui/Cargo.toml`
  Expected: Success.

- [ ] **Step 4: Commit**
  
  ```bash
  git add clients/aether-tui/src/ui/brainstorm_wizard.rs
  git commit -m "style: apply rounded borders and violet accents to Brainstorm Wizard"
  ```

---

### Task 8: Build and E2E Verify UI Output

**Files:**
- Test: Full build and run verification

- [ ] **Step 1: Build release binary**
  
  Run: `cargo build --release --manifest-path clients/aether-tui/Cargo.toml`
  Expected: Compiles with exit status 0.

- [ ] **Step 2: Launch and verify visually**
  
  Launch: `./clients/aether-tui/target/debug/aether-tui` (or target/release/aether-tui)
  Verify:
  - Deep black background.
  - Bo tròn (Rounded corners) on the input border and overlays.
  - Violet/Pink style accent.
  - Interactive popup windows layout.
