use ratatui::{
    layout::{Constraint, Direction, Layout, Rect},
    style::{Modifier, Style},
    text::{Line, Span},
    widgets::{Block, BorderType, Borders, Clear, Paragraph, Wrap},
    Frame,
};
use crate::app::AppState;
use crate::ui::{bg, border_focus, dim, violet, amber, agent_text, error_col, connected};

pub fn draw_metrics_dashboard(f: &mut Frame, state: &AppState, area: Rect) {
    let popup_area = Rect {
        x: area.x + area.width / 16,
        y: area.y + area.height / 16,
        width: area.width * 14 / 16,
        height: area.height * 14 / 16,
    };

    f.render_widget(Clear, popup_area);

    let outer_block = Block::default()
        .borders(Borders::ALL)
        .border_type(BorderType::Rounded)
        .border_style(Style::default().fg(border_focus()))
        .style(Style::default().bg(bg()))
        .title(" 📈 Self-Improvement Metrics (F10) ");

    let inner_area = outer_block.inner(popup_area);
    f.render_widget(outer_block, popup_area);

    let layout = Layout::default()
        .direction(Direction::Vertical)
        .constraints([Constraint::Length(5), Constraint::Min(3), Constraint::Length(3)])
        .split(inner_area);

    // Top: status summary
    let evolution_status = state.metrics.as_ref()
        .and_then(|v| v["evolution_status"].as_str())
        .unwrap_or("(unknown)");
    let pipeline_tracker = state.metrics.as_ref()
        .and_then(|v| v["pipeline_tracker"].as_str())
        .unwrap_or("(unknown)");
    let benchmark_count = state.metrics.as_ref()
        .and_then(|v| v["benchmarks"].as_array().map(|a| a.len()))
        .unwrap_or(0);

    let summary_lines = vec![
        Line::from(vec![
            Span::styled("Evolution: ", Style::default().fg(dim())),
            Span::styled(evolution_status.to_string(), Style::default().fg(amber()).add_modifier(Modifier::BOLD)),
        ]),
        Line::from(vec![
            Span::styled("Pipeline:  ", Style::default().fg(dim())),
            Span::styled(pipeline_tracker.to_string(), Style::default().fg(violet())),
        ]),
        Line::from(vec![
            Span::styled("Benchmarks: ", Style::default().fg(dim())),
            Span::styled(benchmark_count.to_string(), Style::default().fg(agent_text())),
        ]),
    ];
    let summary_widget = Paragraph::new(summary_lines)
        .block(Block::default().borders(Borders::BOTTOM).border_style(Style::default().fg(dim())))
        .style(Style::default().bg(bg()));
    f.render_widget(summary_widget, layout[0]);

    // Middle: benchmark runs list (scrollable)
    let benchmarks: Vec<&serde_json::Value> = state.metrics.as_ref()
        .and_then(|v| v["benchmarks"].as_array())
        .map(|a| a.iter().collect())
        .unwrap_or_default();

    let mut lines: Vec<Line> = Vec::new();
    if state.metrics.is_none() {
        lines.push(Line::from(Span::styled("⏳ Loading metrics...", Style::default().fg(dim()))));
    } else if benchmarks.is_empty() {
        lines.push(Line::from(Span::styled("📭 No benchmark runs recorded", Style::default().fg(dim()))));
    } else {
        // Header
        lines.push(Line::from(vec![
            Span::styled(format!("{:<30} {:>10} {:<20}\n", "Benchmark", "Score", "Timestamp"),
                Style::default().fg(dim()).add_modifier(Modifier::BOLD)),
        ]));
        for b in &benchmarks {
            let name = b["name"].as_str().or_else(|| b["benchmark"].as_str()).unwrap_or("(unknown)");
            let score = b["score"].as_f64()
                .or_else(|| b["score"].as_str().and_then(|s| s.parse().ok()))
                .unwrap_or(0.0);
            let timestamp = b["timestamp"].as_str().unwrap_or("");
            let score_color = if score >= 0.9 { connected() }
                              else if score >= 0.7 { amber() }
                              else { error_col() };
            lines.push(Line::from(vec![
                Span::styled(format!("{:<30} ", truncate_str(name, 30)),
                    Style::default().fg(violet())),
                Span::styled(format!("{:>10.4} ", score),
                    Style::default().fg(score_color).add_modifier(Modifier::BOLD)),
                Span::styled(timestamp.to_string(),
                    Style::default().fg(dim())),
            ]));
        }
    }

    let metrics_widget = Paragraph::new(lines)
        .scroll((state.metrics_scroll.min(u16::MAX as usize) as u16, 0))
        .wrap(Wrap { trim: false })
        .style(Style::default().bg(bg()));
    f.render_widget(metrics_widget, layout[1]);

    // Bottom: help
    let help = Paragraph::new(" [↑/↓] Scroll | [PgUp/PgDn] Page | [r] Refresh | [Esc/F10] Close ")
        .alignment(ratatui::layout::Alignment::Center)
        .style(Style::default().fg(dim()).bg(bg()));
    f.render_widget(help, layout[2]);
}

fn truncate_str(s: &str, max: usize) -> String {
    if s.chars().count() <= max {
        s.to_string()
    } else {
        let truncated: String = s.chars().take(max.saturating_sub(1)).collect();
        format!("{}…", truncated)
    }
}
