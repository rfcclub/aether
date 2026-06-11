use ratatui::{
    Frame,
    layout::{Alignment, Constraint, Direction, Layout, Rect},
    style::{Color, Modifier, Style},
    text::{Line, Span, Text},
    widgets::{Block, BorderType, Borders, Clear, List, ListItem, Paragraph, Wrap},
};

use crate::app::{AppMode, AppState};
use crate::events::Role;

pub mod context_manager;
pub mod brainstorm_wizard;
pub mod git_dashboard;

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

const WAITING_PHRASES: &[&str] = &[
    "Generating..",
    "Loading..",
    "Thinking..",
    "Formulating answers..",
    "Flabbergasting..",
    "Calibrating synapse arrays..",
    "Consulting the digital oracle..",
    "Compiling stardust..",
    "Stoking the cosmic forge..",
    "Summoning Athanor flames..",
    "Infusing thermal energy..",
    "Channelling the inner scholar..",
    "Untangling quantum threads..",
    "Brewing digital espresso..",
    "Consulting local spirits..",
    "Polishing CLI chrome..",
    "Reticulating splines..",
    "Injecting high-density thoughts..",
    "Re-igniting the atomic core..",
    "Whispering to the compiler..",
];

/// Main draw entry point — called every frame
pub fn draw(f: &mut Frame, state: &AppState) {
    let area = f.size();

    // Fill background
    f.render_widget(
        Block::default().style(Style::default().bg(BG)),
        area,
    );

    // ── Vertical layout: header | chat | sep | input | status ─────────────────
    let input_lines = state.input.split('\n').count().max(1);
    let input_height = (input_lines as u16 + 2).min(8);

    let chunks = Layout::default()
        .direction(Direction::Vertical)
        .constraints([
            Constraint::Length(1),   // header
            Constraint::Min(3),      // chat area
            Constraint::Length(1),   // separator
            Constraint::Length(input_height),   // input bar
            Constraint::Length(1),   // status bar
        ])
        .split(area);

    draw_header(f, state, chunks[0]);
    draw_chat(f, state, chunks[1]);
    draw_separator(f, state, chunks[2]);
    draw_input(f, state, chunks[3]);
    draw_status(f, state, chunks[4]);

    // Help overlay (modal)
    if state.mode == AppMode::ShowHelp {
        draw_help_popup(f, area);
    }

    // Model picker overlay (drawn last, on top of everything)
    if state.mode == AppMode::ModelPicker {
        draw_model_picker(f, state, area);
    }

    // Agent picker overlay
    if state.mode == AppMode::AgentPicker {
        draw_agent_picker(f, state, area);
    }

    // Context Manager overlay
    if state.mode == AppMode::ContextManager {
        context_manager::draw_context_manager(f, state, area);
    }

    // Brainstorm Wizard overlay
    if state.mode == AppMode::BrainstormWizard {
        brainstorm_wizard::draw_brainstorm_wizard(f, state, area);
    }

    // Git Dashboard overlay
    if state.mode == AppMode::GitDashboard {
        git_dashboard::draw_git_dashboard(f, state, area);
    }
}

// ── Header ────────────────────────────────────────────────────────────────────
fn draw_header(f: &mut Frame, state: &AppState, area: Rect) {
    let agent_display = {
        if let Some(agent) = state.agents.iter().find(|a| a.0.eq_ignore_ascii_case(&state.group) || a.1.eq_ignore_ascii_case(&state.group)) {
            format!("{} {}", agent.2, agent.1)
        } else {
            let mut chars = state.group.chars();
            match chars.next() {
                None => "Agent".to_string(),
                Some(first) => first.to_uppercase().collect::<String>() + chars.as_str(),
            }
        }
    };

    let model_info = if let Some(ref models) = state.models {
        let effort = models.think_effort.as_deref().unwrap_or("—");
        format!(" · {} · Think:{}", models.current, effort)
    } else {
        String::new()
    };

    let title = format!(" ─ Aether · {}{} ─ ", agent_display, model_info);

    let header = Paragraph::new(title)
        .style(Style::default().fg(BORDER_FOCUS).bg(BG).add_modifier(Modifier::BOLD))
        .alignment(Alignment::Left);

    f.render_widget(header, area);
}

// ── Chat area ─────────────────────────────────────────────────────────────────
fn draw_chat(f: &mut Frame, state: &AppState, area: Rect) {
    // Build all lines to render
    let mut lines: Vec<Line> = Vec::new();

    // Find the boundary between historical and live messages for the separator
    let last_historical_idx = state.messages.iter().rposition(|m| m.is_historical);
    let first_live_idx = last_historical_idx.map(|i| i + 1).unwrap_or(0);
    let has_separator = last_historical_idx.is_some()
        && first_live_idx < state.messages.len();

    // Dynamic search for agent emoji
    let agent_emoji = if let Some(agent) = state.agents.iter().find(|a| {
        a.0.eq_ignore_ascii_case(&state.group) || a.1.eq_ignore_ascii_case(&state.group)
    }) {
        &agent.2
    } else {
        "🌸"
    };

    for (msg_idx, msg) in state.messages.iter().enumerate() {
        let ts_suffix = if msg.is_historical {
            format!("  ({})", msg.timestamp.format("%H:%M"))
        } else {
            "".to_string()
        };

        match msg.role {
            Role::User => {
                let user_lines: Vec<&str> = msg.content.lines().collect();
                for (i, content_line) in user_lines.iter().enumerate() {
                    let mut spans = Vec::new();
                    if i == 0 {
                        spans.push(Span::styled("> ", Style::default().fg(USER_NAME).add_modifier(Modifier::BOLD)));
                        spans.push(Span::styled(content_line.to_string(), Style::default().fg(USER_TEXT)));
                        if !ts_suffix.is_empty() {
                            spans.push(Span::styled(ts_suffix.clone(), Style::default().fg(DIM).add_modifier(Modifier::DIM)));
                        }
                    } else {
                        spans.push(Span::raw("  "));
                        spans.push(Span::styled(content_line.to_string(), Style::default().fg(USER_TEXT)));
                    }

                    // If historical, dim all spans in this line
                    if msg.is_historical {
                        for s in &mut spans {
                            s.style = s.style.fg(DIM).add_modifier(Modifier::DIM);
                        }
                    }

                    lines.push(Line::from(spans));
                }
                lines.push(Line::from(""));
            }
            Role::Assistant => {
                let is_error = msg.content.starts_with("⚠ Error:");
                let text_color = if is_error { ERROR_COL } else { AGENT_TEXT };

                let mut in_code_block = false;
                let assistant_lines: Vec<&str> = msg.content.lines().collect();
                for (i, content_line) in assistant_lines.iter().enumerate() {
                    let mut spans = Vec::new();

                    if content_line.starts_with("```") {
                        in_code_block = !in_code_block;
                        let border_style = Style::default().fg(DIM).add_modifier(Modifier::DIM);
                        let prefix = if i == 0 {
                            format!("{} ", agent_emoji)
                        } else {
                            "   ".to_string()
                        };
                        spans.push(Span::raw(prefix));
                        spans.push(Span::styled(content_line.to_string(), border_style));
                        if i == 0 && !ts_suffix.is_empty() {
                            spans.push(Span::styled(ts_suffix.clone(), Style::default().fg(DIM).add_modifier(Modifier::DIM)));
                        }
                    } else if in_code_block {
                        let prefix = if i == 0 {
                            format!("{} ", agent_emoji)
                        } else {
                            "   ".to_string()
                        };
                        spans.push(Span::raw(prefix));
                        let code_line = highlight_code_line(content_line);
                        spans.extend(code_line.spans);
                        if i == 0 && !ts_suffix.is_empty() {
                            spans.push(Span::styled(ts_suffix.clone(), Style::default().fg(DIM).add_modifier(Modifier::DIM)));
                        }
                    } else {
                        // Parse list items & quotes
                        let is_bullet = content_line.trim_start().starts_with("- ") || content_line.trim_start().starts_with("* ");
                        let is_quote = content_line.trim_start().starts_with("> ");

                        if i == 0 {
                            spans.push(Span::raw(format!("{} ", agent_emoji)));
                            if is_bullet {
                                spans.push(Span::styled("• ", Style::default().fg(AMBER).add_modifier(Modifier::BOLD)));
                            } else if is_quote {
                                spans.push(Span::styled("│ ", Style::default().fg(VIOLET)));
                            }
                        } else {
                            spans.push(Span::raw("   "));
                            if is_bullet {
                                spans.push(Span::styled("• ", Style::default().fg(AMBER).add_modifier(Modifier::BOLD)));
                            } else if is_quote {
                                spans.push(Span::styled("│ ", Style::default().fg(VIOLET)));
                            }
                        }

                        let content_part = if is_bullet || is_quote {
                            &content_line.trim_start()[2..]
                        } else {
                            content_line
                        };

                        let parsed = parse_markdown_line(content_part, Style::default().fg(text_color));
                        spans.extend(parsed.spans);

                        if i == 0 && !ts_suffix.is_empty() {
                            spans.push(Span::styled(ts_suffix.clone(), Style::default().fg(DIM).add_modifier(Modifier::DIM)));
                        }
                    }

                    // If historical, dim all spans in this line
                    if msg.is_historical {
                        for s in &mut spans {
                            s.style = s.style.fg(DIM).add_modifier(Modifier::DIM);
                        }
                    }

                    lines.push(Line::from(spans));
                }
                lines.push(Line::from(""));
            }
        }

        // Insert "── resumed ──" separator
        if has_separator && msg_idx == last_historical_idx.unwrap() {
            lines.push(Line::from(vec![
                Span::styled(
                    "── resumed ──",
                    Style::default().fg(DIM).add_modifier(Modifier::DIM),
                ),
            ]));
            lines.push(Line::from(""));
        }
    }

    // Render live streaming buffer
    if !state.streaming_buf.is_empty() {
        if state.is_typing || state.tokens_received > 0 {
            lines.push(Line::from(vec![
                Span::styled(format!("  [{} tokens]", state.tokens_received), Style::default().fg(DIM)),
            ]));
        }

        let spinner_frames = &["⣾", "⣽", "⣻", "⢿", "⡿", "⣟", "⣯", "⣷"];
        let spinner = spinner_frames[state.spinner_frame as usize % spinner_frames.len()];

        let buf_lines: Vec<&str> = state.streaming_buf.lines().collect();
        let mut in_code_block = false;
        for (i, content_line) in buf_lines.iter().enumerate() {
            let mut spans = Vec::new();
            let is_last = i == buf_lines.len() - 1;

            if content_line.starts_with("```") {
                in_code_block = !in_code_block;
                let border_style = Style::default().fg(DIM).add_modifier(Modifier::DIM);
                let prefix = if i == 0 {
                    format!("{} {} ", spinner, agent_emoji)
                } else {
                    "     ".to_string()
                };
                let prefix_style = if i == 0 { Style::default().fg(AMBER).add_modifier(Modifier::BOLD) } else { Style::default() };
                spans.push(Span::styled(prefix, prefix_style));
                spans.push(Span::styled(content_line.to_string(), border_style));
            } else if in_code_block {
                let prefix = if i == 0 {
                    format!("{} {} ", spinner, agent_emoji)
                } else {
                    "     ".to_string()
                };
                let prefix_style = if i == 0 { Style::default().fg(AMBER).add_modifier(Modifier::BOLD) } else { Style::default() };
                spans.push(Span::styled(prefix, prefix_style));
                let code_line = highlight_code_line(content_line);
                spans.extend(code_line.spans);
            } else {
                let is_bullet = content_line.trim_start().starts_with("- ") || content_line.trim_start().starts_with("* ");
                let is_quote = content_line.trim_start().starts_with("> ");

                if i == 0 {
                    spans.push(Span::styled(format!("{} ", spinner), Style::default().fg(AMBER).add_modifier(Modifier::BOLD)));
                    spans.push(Span::raw(format!("{} ", agent_emoji)));
                    if is_bullet {
                        spans.push(Span::styled("• ", Style::default().fg(AMBER).add_modifier(Modifier::BOLD)));
                    } else if is_quote {
                        spans.push(Span::styled("│ ", Style::default().fg(VIOLET)));
                    }
                } else {
                    spans.push(Span::raw("     "));
                    if is_bullet {
                        spans.push(Span::styled("• ", Style::default().fg(AMBER).add_modifier(Modifier::BOLD)));
                    } else if is_quote {
                        spans.push(Span::styled("│ ", Style::default().fg(VIOLET)));
                    }
                }

                let content_part = if is_bullet || is_quote {
                    &content_line.trim_start()[2..]
                } else {
                    content_line
                };

                let line_style = Style::default().fg(AGENT_TEXT);
                let parsed = parse_markdown_line(content_part, line_style);
                spans.extend(parsed.spans);
            }

            if is_last {
                spans.push(Span::styled("▋", Style::default().fg(CURSOR_COL)));
            }

            lines.push(Line::from(spans));
        }
        lines.push(Line::from(""));
    } else if state.is_typing {
        let spinner_frames = &["⣾", "⣽", "⣻", "⢿", "⡿", "⣟", "⣯", "⣷"];
        let spinner = spinner_frames[state.spinner_frame as usize % spinner_frames.len()];
        let phrase_idx = (state.spinner_frame as usize / 30) % WAITING_PHRASES.len();
        let phrase = WAITING_PHRASES[phrase_idx];

        // Dòng 1: Hiển thị bộ đếm token ở phía TRÊN dòng chữ chờ
        lines.push(Line::from(vec![
            Span::styled(format!("  [{} tokens]", state.tokens_received), Style::default().fg(DIM)),
        ]));

        // Dòng 2: Con quay Braille, Agent Emoji và cụm từ chờ chạy quét đơn ký tự (single character color sweep)
        let chars: Vec<char> = phrase.chars().collect();
        let l = chars.len();
        let highlight_idx = if l > 1 {
            let cycle = l * 2 - 2;
            let idx = (state.spinner_frame as usize) % cycle;
            if idx < l {
                idx
            } else {
                cycle - idx
            }
        } else {
            0
        };

        let mut spans = vec![
            Span::styled(format!("{} ", spinner), Style::default().fg(AMBER).add_modifier(Modifier::BOLD)),
            Span::styled(format!("{} ", agent_emoji), Style::default()),
        ];

        for (i, &ch) in chars.iter().enumerate() {
            if i == highlight_idx {
                spans.push(Span::styled(
                    ch.to_string(),
                    Style::default().fg(AMBER).add_modifier(Modifier::BOLD),
                ));
            } else {
                spans.push(Span::styled(
                    ch.to_string(),
                    Style::default().fg(BORDER_FOCUS),
                ));
            }
        }

        spans.push(Span::styled("▋", Style::default().fg(CURSOR_COL)));

        lines.push(Line::from(spans));
        lines.push(Line::from(""));
    }

    // Connecting overlay text
    if state.mode == AppMode::Connecting {
        let hint = state.reconnect_hint.as_deref().unwrap_or("connecting…");
        lines.push(Line::from(vec![
            Span::styled(format!("  ○ {}", hint), Style::default().fg(DISCONNECTED)),
        ]));
    }

    // Scroll
    let visible_height = area.height as usize;
    let total_lines = lines.len();
    let scroll = if total_lines > visible_height {
        let max_scroll = total_lines.saturating_sub(visible_height);
        let offset = state.scroll_offset.min(max_scroll);
        max_scroll.saturating_sub(offset)
    } else {
        0
    };

    let text = Text::from(lines);
    let chat = Paragraph::new(text)
        .style(Style::default().bg(BG))
        .wrap(Wrap { trim: false })
        .scroll((scroll as u16, 0));

    f.render_widget(chat, area);
}

// ── Separator ─────────────────────────────────────────────────────────────────
fn draw_separator(f: &mut Frame, _state: &AppState, area: Rect) {
    let sep = Paragraph::new("─".repeat(area.width as usize))
        .style(Style::default().fg(DIM).bg(BG));
    f.render_widget(sep, area);
}

// ── Input bar ─────────────────────────────────────────────────────────────────
fn draw_input(f: &mut Frame, state: &AppState, area: Rect) {
    let border_color = if state.connected { BORDER_FOCUS } else { DISCONNECTED };

    let input_block = Block::default()
        .borders(Borders::ALL)
        .border_type(BorderType::Rounded)
        .border_style(Style::default().fg(border_color))
        .style(Style::default().bg(BG));

    let inner = input_block.inner(area);
    f.render_widget(input_block, area);

    // Sử dụng ngọn lửa tĩnh '🔥 › ' cực kỳ cá tính cho prompt của Athanor
    let display_text = format!("🔥 › {}", state.input);
    let input_para = Paragraph::new(display_text)
        .style(Style::default().fg(USER_TEXT).bg(BG));

    f.render_widget(input_para, inner);

    // Kích hoạt terminal cursor nhấp nháy thực tế (blinking cursor) của terminal.
    if state.mode == AppMode::Normal || state.mode == AppMode::Connecting {
        let mut current_col = 0;
        let mut current_line = 0;
        let mut is_first_line = true;

        for (idx, c) in state.input.chars().enumerate() {
            if idx == state.cursor_position {
                break;
            }
            if c == '\n' {
                current_line += 1;
                current_col = 0;
                is_first_line = false;
            } else {
                current_col += 1;
            }
        }

        let prompt_width = if is_first_line { 5 } else { 0 };
        let cursor_x = (inner.x + prompt_width + current_col as u16)
            .min(inner.x + inner.width.saturating_sub(1));
        
        let cursor_y = (inner.y + current_line as u16)
            .min(inner.y + inner.height.saturating_sub(1));

        f.set_cursor(cursor_x, cursor_y);
    }
}

// ── Status bar ────────────────────────────────────────────────────────────────
fn draw_status(f: &mut Frame, state: &AppState, area: Rect) {
    let (conn_dot, conn_color) = if state.connected {
        ("●", CONNECTED)
    } else {
        ("○", DISCONNECTED)
    };

    let group_cap = {
        let mut chars = state.group.chars();
        match chars.next() {
            None => String::new(),
            Some(first) => first.to_uppercase().collect::<String>() + chars.as_str(),
        }
    };

    let mode_str = match state.mode {
        AppMode::Connecting  => "Connecting",
        AppMode::Normal      => "Normal",
        AppMode::Scroll      => "Scroll",
        AppMode::ModelPicker => "ModelPicker",
        AppMode::AgentPicker => "AgentPicker",
        AppMode::ShowHelp    => "Help",
        AppMode::ContextManager => "Context",
        AppMode::BrainstormWizard => "Brainstorm",
        AppMode::GitDashboard => "Git",
    };

    let (mode_text, mode_style) = if state.is_typing {
        (format!("[thinking · tokens: {}]", state.tokens_received), Style::default().fg(AMBER).add_modifier(Modifier::BOLD))
    } else if state.connected && state.tokens_received > 0 {
        (format!("[Normal · {} tokens]", state.tokens_received), Style::default().fg(DIM))
    } else {
        (format!("[{}]", mode_str), Style::default().fg(DIM))
    };

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

    if state.mode == AppMode::Scroll {
        spans.push(Span::styled(
            format!("  [SCROLL · offset:{}]", state.scroll_offset),
            Style::default().fg(AMBER).add_modifier(Modifier::BOLD),
        ));
    }

    let status_line = Line::from(spans);
    let status = Paragraph::new(status_line)
        .style(Style::default().bg(BG))
        .alignment(Alignment::Left);

    f.render_widget(status, area);
}

// ── Model Picker Overlay ──────────────────────────────────────────────────────
fn draw_model_picker(f: &mut Frame, state: &AppState, area: Rect) {
    let mut items: Vec<ListItem> = Vec::new();
    let mut flat_idx = 0usize;

    if let Some(ref models) = state.models {
        let current_model = &models.current;
        for provider in &models.providers {
            items.push(ListItem::new(Line::from(vec![
                Span::styled(
                    format!("  {} ", provider.name),
                    Style::default().fg(PICKER_HDR).add_modifier(Modifier::BOLD),
                ),
            ])));

            for model in &provider.models {
                let is_selected = flat_idx == state.picker_selection;
                let is_current = model == current_model;

                let indicator = if is_current { " ← now" } else { "" };
                let label = format!("    {}{}", model, indicator);

                let style = if is_selected {
                    Style::default().fg(PICKER_SEL).add_modifier(Modifier::BOLD).bg(Color::Rgb(40, 20, 0))
                } else if is_current {
                    Style::default().fg(CONNECTED)
                } else {
                    Style::default().fg(USER_TEXT)
                };

                items.push(ListItem::new(Line::from(vec![
                    Span::styled(label, style),
                ])));
                flat_idx += 1;
            }
        }
    } else {
        items.push(ListItem::new(Line::from(vec![
            Span::styled("  Loading…", Style::default().fg(DIM)),
        ])));
    }

    let popup_width = ((area.width as usize * 50 / 100).max(40) as u16).min(area.width.saturating_sub(4));
    let popup_height = ((items.len() + 4) as u16).min(20).min(area.height.saturating_sub(4));

    let popup_x = (area.width.saturating_sub(popup_width)) / 2;
    let popup_y = (area.height.saturating_sub(popup_height)) / 2;

    let popup_area = Rect {
        x: area.x + popup_x,
        y: area.y + popup_y,
        width: popup_width,
        height: popup_height,
    };

    f.render_widget(Clear, popup_area);

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

    let inner = block.inner(popup_area);
    f.render_widget(block, popup_area);

    let list = List::new(items)
        .style(Style::default().bg(BG));

    f.render_widget(list, inner);
}

// ── Agent Picker Overlay ──────────────────────────────────────────────────────
fn draw_agent_picker(f: &mut Frame, state: &AppState, area: Rect) {
    let mut items: Vec<ListItem> = Vec::new();
    for (idx, (name, display, emoji)) in state.agents.iter().enumerate() {
        let is_selected = idx == state.agent_selection;
        let is_current = name == &state.group;

        let indicator = if is_current { " (current)" } else { "" };
        let label = format!("  {}  {} ({}){}", emoji, display, name, indicator);

        let style = if is_selected {
            Style::default().fg(PICKER_SEL).add_modifier(Modifier::BOLD).bg(Color::Rgb(40, 20, 0))
        } else if is_current {
            Style::default().fg(CONNECTED)
        } else {
            Style::default().fg(USER_TEXT)
        };

        items.push(ListItem::new(Line::from(vec![
            Span::styled(label, style),
        ])));
    }

    if items.is_empty() {
        items.push(ListItem::new(Line::from(vec![
            Span::styled("  No agents found", Style::default().fg(DIM)),
        ])));
    }

    let popup_width = ((area.width as usize * 40 / 100).max(40) as u16).min(area.width.saturating_sub(4));
    let popup_height = ((items.len() + 4) as u16).min(15).min(area.height.saturating_sub(4));

    let popup_x = (area.width.saturating_sub(popup_width)) / 2;
    let popup_y = (area.height.saturating_sub(popup_height)) / 2;

    let popup_area = Rect {
        x: area.x + popup_x,
        y: area.y + popup_y,
        width: popup_width,
        height: popup_height,
    };

    f.render_widget(Clear, popup_area);

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

    let inner = block.inner(popup_area);
    f.render_widget(block, popup_area);

    let list = List::new(items).style(Style::default().bg(BG));
    f.render_widget(list, inner);
}

// ── Help popup ────────────────────────────────────────────────────────────────
fn draw_help_popup(f: &mut Frame, area: Rect) {
    let popup_width = 54u16.min(area.width.saturating_sub(4));
    let popup_height = 26u16.min(area.height.saturating_sub(4));

    let popup_x = (area.width.saturating_sub(popup_width)) / 2;
    let popup_y = (area.height.saturating_sub(popup_height)) / 2;

    let popup_area = Rect {
        x: area.x + popup_x,
        y: area.y + popup_y,
        width: popup_width,
        height: popup_height,
    };

    f.render_widget(Clear, popup_area);

    let help_text = vec![
        Line::from(vec![Span::styled("  Aether TUI — Keybindings", Style::default().fg(AGENT_NAME).add_modifier(Modifier::BOLD))]),
        Line::from(""),
        Line::from(vec![
            Span::styled("  Enter      ", Style::default().fg(USER_NAME)),
            Span::styled("Send message", Style::default().fg(USER_TEXT)),
        ]),
        Line::from(vec![
            Span::styled("  Alt+Enter  ", Style::default().fg(USER_NAME)),
            Span::styled("Insert newline (multiline chatbox)", Style::default().fg(USER_TEXT)),
        ]),
        Line::from(vec![
            Span::styled("  Ctrl+Q     ", Style::default().fg(USER_NAME)),
            Span::styled("Quit", Style::default().fg(USER_TEXT)),
        ]),
        Line::from(vec![
            Span::styled("  Ctrl+L     ", Style::default().fg(USER_NAME)),
            Span::styled("Clear messages", Style::default().fg(USER_TEXT)),
        ]),
        Line::from(vec![
            Span::styled("  Esc        ", Style::default().fg(USER_NAME)),
            Span::styled("Scroll mode  (j/k/PgUp/PgDn/G/gg)", Style::default().fg(USER_TEXT)),
        ]),
        Line::from(vec![
            Span::styled("  F2 / Ctrl+M", Style::default().fg(USER_NAME)),
            Span::styled("Model picker", Style::default().fg(USER_TEXT)),
        ]),
        Line::from(vec![
            Span::styled("  F3 / Ctrl+A", Style::default().fg(USER_NAME)),
            Span::styled("Agent picker", Style::default().fg(USER_TEXT)),
        ]),
        Line::from(vec![
            Span::styled("  F4         ", Style::default().fg(USER_NAME)),
            Span::styled("Context files manager", Style::default().fg(USER_TEXT)),
        ]),
        Line::from(vec![
            Span::styled("  F5         ", Style::default().fg(USER_NAME)),
            Span::styled("Socratic brainstorming wizard", Style::default().fg(USER_TEXT)),
        ]),
        Line::from(vec![
            Span::styled("  F6         ", Style::default().fg(USER_NAME)),
            Span::styled("Inject TDD template into chatbox", Style::default().fg(USER_TEXT)),
        ]),
        Line::from(vec![
            Span::styled("  F7         ", Style::default().fg(USER_NAME)),
            Span::styled("Interactive git dashboard", Style::default().fg(USER_TEXT)),
        ]),
        Line::from(vec![
            Span::styled("  F1 / ?     ", Style::default().fg(USER_NAME)),
            Span::styled("This help", Style::default().fg(USER_TEXT)),
        ]),
        Line::from(""),
        Line::from(vec![Span::styled("  Slash Commands", Style::default().fg(AGENT_NAME).add_modifier(Modifier::BOLD))]),
        Line::from(""),
        Line::from(vec![
            Span::styled("  /clear     ", Style::default().fg(USER_NAME)),
            Span::styled("Clear display (local)", Style::default().fg(USER_TEXT)),
        ]),
        Line::from(vec![
            Span::styled("  /help      ", Style::default().fg(USER_NAME)),
            Span::styled("Show this panel", Style::default().fg(USER_TEXT)),
        ]),
        Line::from(vec![
            Span::styled("  /quit /q   ", Style::default().fg(USER_NAME)),
            Span::styled("Exit", Style::default().fg(USER_TEXT)),
        ]),
        Line::from(vec![
            Span::styled("  /model … ", Style::default().fg(USER_NAME)),
            Span::styled("Forwarded to server", Style::default().fg(DIM)),
        ]),
        Line::from(""),
        Line::from(vec![Span::styled("  Press any key to close", Style::default().fg(DIM))]),
    ];

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

    f.render_widget(help_para, popup_area);
}

// ── Markdown Parser Helper ────────────────────────────────────────────────────
fn parse_markdown_line(text: &str, default_style: Style) -> Line<'static> {
    let mut spans = vec![];
    let mut current = String::new();
    let mut chars = text.chars().peekable();

    while let Some(c) = chars.next() {
        if c == '*' && chars.peek() == Some(&'*') {
            if !current.is_empty() {
                spans.push(Span::styled(std::mem::take(&mut current), default_style));
            }
            chars.next(); // Consume second '*'

            let mut bold_text = String::new();
            while let Some(bc) = chars.next() {
                if bc == '*' && chars.peek() == Some(&'*') {
                    chars.next();
                    break;
                }
                bold_text.push(bc);
            }
            spans.push(Span::styled(bold_text, default_style.add_modifier(Modifier::BOLD)));
        } else if c == '`' {
            if !current.is_empty() {
                spans.push(Span::styled(std::mem::take(&mut current), default_style));
            }

            let mut code_text = String::new();
            while let Some(cc) = chars.next() {
                if cc == '`' {
                    break;
                }
                code_text.push(cc);
            }
            spans.push(Span::styled(
                code_text,
                Style::default().fg(Color::Rgb(255, 128, 0)).bg(Color::Rgb(25, 25, 25))
            ));
        } else {
            current.push(c);
        }
    }

    if !current.is_empty() {
        spans.push(Span::styled(current, default_style));
    }

    Line::from(spans)
}

// ── Code Syntax Highlighting Helper ───────────────────────────────────────────
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
