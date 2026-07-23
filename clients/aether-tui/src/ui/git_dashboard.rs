use ratatui::{
    layout::{Constraint, Direction, Layout, Rect},
    style::Style,
    text::{Line, Span},
    widgets::{Block, BorderType, Borders, Clear, Paragraph},
    Frame,
};
use crate::app::AppState;
use crate::ui::{bg, border_focus, dim, violet, connected, error_col, user_name};

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
        .border_type(BorderType::Rounded)
        .border_style(Style::default().fg(border_focus()))
        .style(Style::default().bg(bg()))
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
            Style::default().fg(connected())
        } else {
            Style::default().fg(error_col())
        };
        file_lines.push(Line::from(vec![
            Span::raw(cursor),
            Span::styled(format!("[{}] ", status), style),
            Span::styled(path.clone(), Style::default().fg(violet())),
        ]));
    }

    let files_widget = Paragraph::new(file_lines)
        .block(Block::default().borders(Borders::RIGHT).border_style(Style::default().fg(dim())))
        .style(Style::default().bg(bg()));
    f.render_widget(files_widget, columns[0]);

    // Right panel: code inline diff
    let mut diff_lines = Vec::new();
    for line in state.selected_diff.lines() {
        let style = if line.starts_with('+') && !line.starts_with("+++") {
            Style::default().fg(connected())
        } else if line.starts_with('-') && !line.starts_with("---") {
            Style::default().fg(error_col())
        } else if line.starts_with("@@") {
            Style::default().fg(user_name()) // Ice blue
        } else {
            Style::default().fg(dim())
        };
        diff_lines.push(Line::from(Span::styled(line.to_string(), style)));
    }

    let diff_widget = Paragraph::new(diff_lines)
        .style(Style::default().bg(bg()));
    f.render_widget(diff_widget, columns[1]);

    // Bottom bar: shortcuts
    let help = Paragraph::new(" [Space] Stage/Unstage | [c] Guided Commit | [Esc/F7] Close Dashboard ")
        .alignment(ratatui::layout::Alignment::Center)
        .style(Style::default().fg(dim()).bg(bg()));
    f.render_widget(help, layout[1]);
}
