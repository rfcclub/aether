use ratatui::{
    layout::{Constraint, Direction, Layout, Rect},
    style::{Modifier, Style},
    text::{Line, Span},
    widgets::{Block, BorderType, Borders, Clear, List, ListItem, Paragraph},
    Frame,
};
use crate::app::AppState;
use crate::ui::{bg, border_focus, dim, violet, amber, agent_text, connected};

pub fn draw_skills_panel(f: &mut Frame, state: &AppState, area: Rect) {
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
        .title(" 🐝 Skills Panel (F9) ");

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

    let skills_arr: Vec<&serde_json::Value> = state.skills.as_ref()
        .and_then(|v| v["skills"].as_array())
        .map(|a| a.iter().collect())
        .unwrap_or_default();

    let mut items: Vec<ListItem> = Vec::new();
    if skills_arr.is_empty() {
        let label = match &state.skills {
            None => "⏳ Loading skills...",
            Some(v) if v.get("error").is_some() => "⚠ Failed to load skills",
            _ => "📭 No skills registered",
        };
        items.push(ListItem::new(Span::styled(label, Style::default().fg(dim()))));
    } else {
        for (idx, s) in skills_arr.iter().enumerate() {
            let cursor = if idx == state.skills_selection { "➔ " } else { "  " };
            let name = s["name"].as_str().or_else(|| s["id"].as_str()).unwrap_or("(unknown)");
            let trigger = s["trigger"].as_str().unwrap_or("");
            items.push(ListItem::new(Line::from(vec![
                Span::raw(cursor.to_string()),
                Span::styled(name.to_string(), Style::default().fg(violet()).add_modifier(Modifier::BOLD)),
                Span::raw("  "),
                Span::styled(format!("/{}", trigger), Style::default().fg(amber())),
            ])));
        }
    }

    let list_widget = List::new(items)
        .block(Block::default().borders(Borders::RIGHT).border_style(Style::default().fg(dim())))
        .style(Style::default().bg(bg()));
    f.render_widget(list_widget, cols[0]);

    let selected = skills_arr.get(state.skills_selection);
    let detail_lines: Vec<Line> = if let Some(s) = selected {
        let mut lines = Vec::new();
        if let Some(id) = s["id"].as_str() {
            lines.push(Line::from(vec![
                Span::styled("ID: ", Style::default().fg(dim())),
                Span::styled(id.to_string(), Style::default().fg(agent_text())),
            ]));
        }
        if let Some(name) = s["name"].as_str() {
            lines.push(Line::from(vec![
                Span::styled("Name: ", Style::default().fg(dim())),
                Span::styled(name.to_string(), Style::default().fg(violet()).add_modifier(Modifier::BOLD)),
            ]));
        }
        if let Some(trigger) = s["trigger"].as_str() {
            lines.push(Line::from(vec![
                Span::styled("Trigger: ", Style::default().fg(dim())),
                Span::styled(format!("/{}", trigger), Style::default().fg(amber())),
            ]));
        }
        if let Some(desc) = s["description"].as_str() {
            lines.push(Line::from(""));
            lines.push(Line::from(Span::styled("Description:", Style::default().fg(dim()))));
            for ln in desc.lines() {
                lines.push(Line::from(Span::styled(ln.to_string(), Style::default().fg(agent_text()))));
            }
        }
        if let Some(status) = s["status"].as_str() {
            lines.push(Line::from(""));
            lines.push(Line::from(vec![
                Span::styled("Status: ", Style::default().fg(dim())),
                Span::styled(status.to_string(), Style::default().fg(connected())),
            ]));
        }
        if lines.is_empty() {
            lines.push(Line::from(Span::styled("(no detail)", Style::default().fg(dim()))));
        }
        lines
    } else {
        vec![Line::from(Span::styled("Select a skill to view details", Style::default().fg(dim())))]
    };

    let detail_widget = Paragraph::new(detail_lines).style(Style::default().bg(bg()));
    f.render_widget(detail_widget, cols[1]);

    let help = Paragraph::new(" [↑/↓] Navigate | [r] Refresh | [Esc/F9] Close ")
        .alignment(ratatui::layout::Alignment::Center)
        .style(Style::default().fg(dim()).bg(bg()));
    f.render_widget(help, layout[1]);
}
