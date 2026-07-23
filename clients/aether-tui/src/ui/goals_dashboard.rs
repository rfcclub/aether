use ratatui::{
    layout::{Constraint, Direction, Layout, Rect},
    style::{Modifier, Style},
    text::{Line, Span},
    widgets::{Block, BorderType, Borders, Clear, List, ListItem, Paragraph},
    Frame,
};
use crate::app::AppState;
use crate::ui::{bg, border_focus, dim, violet, amber, agent_text, error_col, connected};

pub fn draw_goals_dashboard(f: &mut Frame, state: &AppState, area: Rect) {
    let popup_area = Rect {
        x: area.x + area.width / 12,
        y: area.y + area.height / 12,
        width: area.width * 5 / 6,
        height: area.height * 5 / 6,
    };

    f.render_widget(Clear, popup_area);

    let outer_block = Block::default()
        .borders(Borders::ALL)
        .border_type(BorderType::Rounded)
        .border_style(Style::default().fg(border_focus()))
        .style(Style::default().bg(bg()))
        .title(" 🎯 Goals Dashboard (F8) ");

    let inner_area = outer_block.inner(popup_area);
    f.render_widget(outer_block, popup_area);

    let layout = Layout::default()
        .direction(Direction::Vertical)
        .constraints([Constraint::Min(3), Constraint::Length(3)])
        .split(inner_area);

    let cols = Layout::default()
        .direction(Direction::Horizontal)
        .constraints([Constraint::Percentage(40), Constraint::Percentage(60)])
        .split(layout[0]);

    let goals_arr: Vec<&serde_json::Value> = state.goals.as_ref()
        .and_then(|v| v["goals"].as_array())
        .map(|a| a.iter().collect())
        .unwrap_or_default();

    let mut items: Vec<ListItem> = Vec::new();
    if goals_arr.is_empty() {
        let label = match &state.goals {
            None => "⏳ Loading goals...",
            Some(v) if v.get("error").is_some() => "⚠ Failed to load goals",
            _ => "📭 No goals defined",
        };
        items.push(ListItem::new(Span::styled(label, Style::default().fg(dim()))));
    } else {
        for (idx, g) in goals_arr.iter().enumerate() {
            let cursor = if idx == state.goals_selection { "➔ " } else { "  " };
            let title = g["title"].as_str().unwrap_or("(untitled)");
            let status = g["status"].as_str().unwrap_or("Unknown");
            let priority = g["priority"].as_i64().unwrap_or(0);
            let status_style = match status {
                "Completed" | "completed" => Style::default().fg(connected()),
                "Failed" | "failed" | "Blocked" | "blocked" => Style::default().fg(error_col()),
                _ => Style::default().fg(amber()),
            };
            let prio_color = match priority {
                p if p >= 8 => error_col(),
                p if p >= 5 => amber(),
                _ => dim(),
            };
            items.push(ListItem::new(Line::from(vec![
                Span::raw(cursor.to_string()),
                Span::styled(format!("[{}] ", status), status_style),
                Span::styled(format!("P{} ", priority), Style::default().fg(prio_color)),
                Span::styled(title.to_string(), Style::default().fg(violet())),
            ])));
        }
    }

    let list_widget = List::new(items)
        .block(Block::default().borders(Borders::RIGHT).border_style(Style::default().fg(dim())))
        .style(Style::default().bg(bg()));
    f.render_widget(list_widget, cols[0]);

    let selected = goals_arr.get(state.goals_selection);
    let detail_lines: Vec<Line> = if let Some(g) = selected {
        let mut lines = Vec::new();
        if let Some(id) = g["id"].as_str() {
            lines.push(Line::from(vec![
                Span::styled("ID: ", Style::default().fg(dim())),
                Span::styled(id.to_string(), Style::default().fg(agent_text())),
            ]));
        }
        if let Some(title) = g["title"].as_str() {
            lines.push(Line::from(vec![
                Span::styled("Title: ", Style::default().fg(dim())),
                Span::styled(title.to_string(), Style::default().fg(violet()).add_modifier(Modifier::BOLD)),
            ]));
        }
        if let Some(status) = g["status"].as_str() {
            lines.push(Line::from(vec![
                Span::styled("Status: ", Style::default().fg(dim())),
                Span::styled(status.to_string(), Style::default().fg(amber())),
            ]));
        }
        if let Some(p) = g["priority"].as_i64() {
            lines.push(Line::from(vec![
                Span::styled("Priority: ", Style::default().fg(dim())),
                Span::styled(p.to_string(), Style::default().fg(agent_text())),
            ]));
        }
        if let Some(t) = g["tension"].as_i64() {
            lines.push(Line::from(vec![
                Span::styled("Tension: ", Style::default().fg(dim())),
                Span::styled(t.to_string(), Style::default().fg(error_col())),
            ]));
        }
        if let Some(desc) = g["description"].as_str() {
            lines.push(Line::from(""));
            lines.push(Line::from(Span::styled("Description:", Style::default().fg(dim()))));
            for ln in desc.lines() {
                lines.push(Line::from(Span::styled(ln.to_string(), Style::default().fg(agent_text()))));
            }
        }
        if lines.is_empty() {
            lines.push(Line::from(Span::styled("(no detail)", Style::default().fg(dim()))));
        }
        lines
    } else {
        vec![Line::from(Span::styled("Select a goal to view details", Style::default().fg(dim())))]
    };

    let detail_widget = Paragraph::new(detail_lines).style(Style::default().bg(bg()));
    f.render_widget(detail_widget, cols[1]);

    let help = Paragraph::new(" [↑/↓] Navigate | [r] Refresh | [Esc/F8] Close ")
        .alignment(ratatui::layout::Alignment::Center)
        .style(Style::default().fg(dim()).bg(bg()));
    f.render_widget(help, layout[1]);
}
