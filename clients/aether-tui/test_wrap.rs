use ratatui::widgets::{Paragraph, Wrap};
use ratatui::text::Text;

fn main() {
    let text = Text::from("Hello world\nThis is a test.");
    let p = Paragraph::new(text).wrap(Wrap { trim: false });
    let cnt = p.line_count(10);
    println!("cnt = {}", cnt);
}
