use ratatui::{
    layout::{Constraint, Direction, Layout, Rect},
    style::{Color, Style},
    widgets::{Block, BorderType, Borders, Clear, List, ListItem, Paragraph},
    Frame,
};
use crate::app::AppState;
use crate::ui::{BG, BORDER_FOCUS, AMBER, DIM, VIOLET};

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
        .border_type(BorderType::Rounded)
        .border_style(Style::default().fg(BORDER_FOCUS))
        .style(Style::default().bg(BG))
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
                Style::default().fg(Color::Black).bg(AMBER)
            } else {
                Style::default().fg(VIOLET)
            };
            ListItem::new(file.as_str()).style(style)
        })
        .collect();

    let list = List::new(items)
        .block(Block::default().borders(Borders::BOTTOM).border_style(Style::default().fg(DIM)))
        .style(Style::default().bg(BG));
    f.render_widget(list, chunks[0]);

    let help_msg = " [a] Add File | [d] Delete | [c] Clear All | [Esc/F4] Close Overlay ";
    let help_widget = Paragraph::new(help_msg)
        .alignment(ratatui::layout::Alignment::Center)
        .style(Style::default().fg(DIM).bg(BG));
    f.render_widget(help_widget, chunks[1]);

    if state.show_input_dialog {
        let dialog_area = Rect {
            x: popup_area.x + popup_area.width / 4,
            y: popup_area.y + popup_area.height / 3,
            width: popup_area.width / 2,
            height: 5,
        };
        f.render_widget(Clear, dialog_area);
        
        let block = Block::default()
            .borders(Borders::ALL)
            .border_type(BorderType::Rounded)
            .border_style(Style::default().fg(BORDER_FOCUS))
            .style(Style::default().bg(BG))
            .title(" Enter File Path: ");
            
        let inner = block.inner(dialog_area);
        
        let input_len = state.dialog_input.chars().count();
        let max_width = inner.width as usize;
        let displayed_input: String = if input_len > max_width {
            state.dialog_input.chars().skip(input_len - max_width).collect()
        } else {
            state.dialog_input.clone()
        };
        
        let input_box = Paragraph::new(displayed_input)
            .block(block);
        f.render_widget(input_box, dialog_area);

        let cursor_col = if input_len > max_width {
            max_width as u16
        } else {
            input_len as u16
        };
        let cursor_x = (inner.x + cursor_col)
            .min(inner.x + inner.width.saturating_sub(1));
        let cursor_y = inner.y;
        f.set_cursor(cursor_x, cursor_y);
    }
}
