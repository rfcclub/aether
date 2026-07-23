use ratatui::style::Color;
use std::sync::RwLock;

#[derive(Clone, Copy)]
pub struct Theme {
    pub bg: Color,
    pub user_name: Color,
    pub user_text: Color,
    pub agent_name: Color,
    pub agent_text: Color,
    pub cursor_col: Color,
    pub border_focus: Color,
    pub connected: Color,
    pub disconnected: Color,
    pub error_col: Color,
    pub dim: Color,
    pub amber: Color,
    pub violet: Color,
    pub picker_sel: Color,
    pub picker_hdr: Color,
}

impl Theme {
    pub const fn forge() -> Self {
        Self {
            bg: Color::Rgb(8, 8, 8),
            user_name: Color::Rgb(91, 200, 245),
            user_text: Color::Rgb(220, 220, 220),
            agent_name: Color::Rgb(236, 72, 153),
            agent_text: Color::Rgb(243, 232, 255),
            cursor_col: Color::Rgb(236, 72, 153),
            border_focus: Color::Rgb(168, 85, 247),
            connected: Color::Rgb(68, 255, 136),
            disconnected: Color::Rgb(80, 80, 80),
            error_col: Color::Rgb(255, 80, 80),
            dim: Color::Rgb(120, 110, 130),
            amber: Color::Rgb(255, 140, 0),
            violet: Color::Rgb(168, 85, 247),
            picker_sel: Color::Rgb(168, 85, 247),
            picker_hdr: Color::Rgb(180, 180, 180),
        }
    }

    pub const fn matrix() -> Self {
        Self {
            bg: Color::Rgb(10, 10, 10),
            user_name: Color::Rgb(68, 255, 68),
            user_text: Color::Rgb(0, 255, 0),
            agent_name: Color::Rgb(0, 255, 0),
            agent_text: Color::Rgb(0, 255, 0),
            cursor_col: Color::Rgb(0, 255, 0),
            border_focus: Color::Rgb(0, 200, 0),
            connected: Color::Rgb(68, 255, 136),
            disconnected: Color::Rgb(80, 80, 80),
            error_col: Color::Rgb(255, 80, 80),
            dim: Color::Rgb(0, 136, 0),
            amber: Color::Rgb(0, 255, 0),
            violet: Color::Rgb(0, 200, 0),
            picker_sel: Color::Rgb(0, 200, 0),
            picker_hdr: Color::Rgb(180, 180, 180),
        }
    }

    pub const fn amber() -> Self {
        Self {
            bg: Color::Rgb(26, 26, 10),
            user_name: Color::Rgb(255, 212, 74),
            user_text: Color::Rgb(255, 176, 0),
            agent_name: Color::Rgb(255, 176, 0),
            agent_text: Color::Rgb(255, 176, 0),
            cursor_col: Color::Rgb(255, 176, 0),
            border_focus: Color::Rgb(255, 176, 0),
            connected: Color::Rgb(68, 255, 136),
            disconnected: Color::Rgb(80, 80, 80),
            error_col: Color::Rgb(255, 80, 80),
            dim: Color::Rgb(138, 108, 42),
            amber: Color::Rgb(255, 176, 0),
            violet: Color::Rgb(255, 176, 0),
            picker_sel: Color::Rgb(255, 176, 0),
            picker_hdr: Color::Rgb(180, 180, 180),
        }
    }

    pub const fn mono() -> Self {
        Self {
            bg: Color::Rgb(26, 26, 26),
            user_name: Color::Rgb(255, 255, 255),
            user_text: Color::Rgb(224, 224, 224),
            agent_name: Color::Rgb(224, 224, 224),
            agent_text: Color::Rgb(224, 224, 224),
            cursor_col: Color::Rgb(255, 255, 255),
            border_focus: Color::Rgb(255, 255, 255),
            connected: Color::Rgb(102, 255, 102),
            disconnected: Color::Rgb(80, 80, 80),
            error_col: Color::Rgb(255, 102, 102),
            dim: Color::Rgb(160, 160, 160),
            amber: Color::Rgb(224, 224, 224),
            violet: Color::Rgb(224, 224, 224),
            picker_sel: Color::Rgb(255, 255, 255),
            picker_hdr: Color::Rgb(180, 180, 180),
        }
    }

    pub const fn high_contrast() -> Self {
        Self {
            bg: Color::Rgb(0, 0, 0),
            user_name: Color::Rgb(255, 255, 0),
            user_text: Color::Rgb(255, 255, 255),
            agent_name: Color::Rgb(0, 255, 255),
            agent_text: Color::Rgb(255, 255, 255),
            cursor_col: Color::Rgb(255, 255, 0),
            border_focus: Color::Rgb(255, 255, 255),
            connected: Color::Rgb(0, 255, 0),
            disconnected: Color::Rgb(128, 128, 128),
            error_col: Color::Rgb(255, 0, 0),
            dim: Color::Rgb(192, 192, 192),
            amber: Color::Rgb(255, 255, 0),
            violet: Color::Rgb(255, 0, 255),
            picker_sel: Color::Rgb(255, 255, 0),
            picker_hdr: Color::Rgb(255, 255, 255),
        }
    }

    pub fn by_name(name: &str) -> Option<Self> {
        match name.to_lowercase().as_str() {
            "forge" | "athanor" | "fire" => Some(Self::forge()),
            "matrix" | "green" => Some(Self::matrix()),
            "amber" => Some(Self::amber()),
            "mono" | "monochrome" => Some(Self::mono()),
            "high-contrast" | "high_contrast" | "hc" => Some(Self::high_contrast()),
            _ => None,
        }
    }
}

pub fn available_themes() -> &'static [&'static str] {
    &["forge", "matrix", "amber", "mono", "high-contrast"]
}

static THEME: RwLock<Theme> = RwLock::new(Theme::forge());

pub fn current() -> Theme {
    THEME.read().unwrap_or_else(|e| e.into_inner()).clone()
}

pub fn set_theme(name: &str) -> bool {
    if let Some(t) = Theme::by_name(name) {
        if let Ok(mut guard) = THEME.write() {
            *guard = t;
            return true;
        }
    }
    false
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_by_name_forge_aliases() {
        assert!(Theme::by_name("forge").is_some());
        assert!(Theme::by_name("athanor").is_some());
        assert!(Theme::by_name("fire").is_some());
        assert!(Theme::by_name("Forge").is_some());
    }

    #[test]
    fn test_by_name_matrix_aliases() {
        assert!(Theme::by_name("matrix").is_some());
        assert!(Theme::by_name("green").is_some());
    }

    #[test]
    fn test_by_name_mono_aliases() {
        assert!(Theme::by_name("mono").is_some());
        assert!(Theme::by_name("monochrome").is_some());
    }

    #[test]
    fn test_by_name_high_contrast_aliases() {
        assert!(Theme::by_name("high-contrast").is_some());
        assert!(Theme::by_name("high_contrast").is_some());
        assert!(Theme::by_name("hc").is_some());
    }

    #[test]
    fn test_by_name_unknown() {
        assert!(Theme::by_name("nonexistent").is_none());
        assert!(Theme::by_name("").is_none());
    }

    #[test]
    fn test_set_theme_returns_true_for_valid() {
        assert!(set_theme("forge"));
        assert!(set_theme("matrix"));
        assert!(set_theme("amber"));
        assert!(set_theme("mono"));
        assert!(set_theme("high-contrast"));
    }

    #[test]
    fn test_set_theme_returns_false_for_invalid() {
        assert!(!set_theme("nonexistent"));
        assert!(!set_theme(""));
    }

    #[test]
    fn test_set_theme_changes_current() {
        set_theme("forge");
        let forge = current();
        set_theme("matrix");
        let matrix = current();
        assert_eq!(forge.user_name, Color::Rgb(91, 200, 245));
        assert_eq!(matrix.user_name, Color::Rgb(68, 255, 68));
    }

    #[test]
    fn test_available_themes_includes_all() {
        let themes = available_themes();
        assert_eq!(themes.len(), 5);
        assert!(themes.contains(&"forge"));
        assert!(themes.contains(&"matrix"));
        assert!(themes.contains(&"amber"));
        assert!(themes.contains(&"mono"));
        assert!(themes.contains(&"high-contrast"));
    }
}
