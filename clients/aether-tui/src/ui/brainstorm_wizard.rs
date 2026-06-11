use ratatui::{
    layout::{Constraint, Direction, Layout, Rect},
    style::{Modifier, Style},
    widgets::{Block, BorderType, Borders, Clear, Paragraph},
    Frame,
};
use crate::app::AppState;
use crate::ui::{BG, BORDER_FOCUS, AMBER, DIM};

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
        .border_type(BorderType::Rounded)
        .border_style(Style::default().fg(BORDER_FOCUS))
        .style(Style::default().bg(BG))
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
        .style(Style::default().fg(AMBER).add_modifier(Modifier::BOLD).bg(BG));
    f.render_widget(question, chunks[0]);

    let answer_block = Block::default()
        .borders(Borders::ALL)
        .border_type(BorderType::Rounded)
        .border_style(Style::default().fg(DIM))
        .style(Style::default().bg(BG));
    let answer_inner = answer_block.inner(chunks[1]);

    let answer_text = &state.brainstorm_answers[state.brainstorm_step];
    let answer_len = answer_text.chars().count();
    let max_width = answer_inner.width as usize;
    let displayed_answer: String = if answer_len > max_width {
        answer_text.chars().skip(answer_len - max_width).collect()
    } else {
        answer_text.clone()
    };

    let answer = Paragraph::new(displayed_answer)
        .block(answer_block);
    f.render_widget(answer, chunks[1]);

    let cursor_col = if answer_len > max_width {
        max_width as u16
    } else {
        answer_len as u16
    };
    let cursor_x = (answer_inner.x + cursor_col)
        .min(answer_inner.x + answer_inner.width.saturating_sub(1));
    let cursor_y = answer_inner.y;
    f.set_cursor(cursor_x, cursor_y);

    let progress = format!(" Step {} / 4 | [Enter] Next Step | [Esc] Cancel Wizard ", state.brainstorm_step + 1);
    let help = Paragraph::new(progress)
        .alignment(ratatui::layout::Alignment::Center)
        .style(Style::default().fg(DIM).bg(BG));
    f.render_widget(help, chunks[2]);
}
