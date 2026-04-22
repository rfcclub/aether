CREATE TABLE IF NOT EXISTS messages (
    id TEXT PRIMARY KEY,
    group_jid TEXT NOT NULL,
    sender TEXT NOT NULL,
    content TEXT NOT NULL,
    timestamp TEXT NOT NULL,
    is_from_me INTEGER NOT NULL DEFAULT 0,
    is_bot_message INTEGER NOT NULL DEFAULT 0,
    session_id TEXT
);

CREATE TABLE IF NOT EXISTS sessions (
    id TEXT PRIMARY KEY,
    group_folder TEXT NOT NULL,
    created_at TEXT NOT NULL,
    last_activity TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS tasks (
    id TEXT PRIMARY KEY,
    group_folder TEXT NOT NULL,
    prompt TEXT NOT NULL,
    script TEXT,
    schedule_type TEXT NOT NULL,
    schedule_value TEXT NOT NULL,
    status TEXT NOT NULL,
    next_run TEXT,
    created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS groups (
    jid TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    folder TEXT NOT NULL,
    is_main INTEGER NOT NULL DEFAULT 0,
    requires_trigger INTEGER NOT NULL DEFAULT 1,
    trigger TEXT,
    container_config TEXT
);

CREATE TABLE IF NOT EXISTS task_runs (
    id TEXT PRIMARY KEY,
    task_id TEXT NOT NULL,
    started_at TEXT NOT NULL,
    completed_at TEXT,
    result TEXT,
    error TEXT,
    FOREIGN KEY (task_id) REFERENCES tasks(id)
);
