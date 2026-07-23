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

-- FTS5 virtual table for full-text search on messages
CREATE VIRTUAL TABLE IF NOT EXISTS messages_fts USING fts5(
    content,
    content=messages,
    content_rowid=rowid
);

-- Trigger to keep FTS5 in sync with messages table
CREATE TRIGGER IF NOT EXISTS messages_ai AFTER INSERT ON messages BEGIN
    INSERT INTO messages_fts(rowid, content) VALUES (new.rowid, new.content);
END;

CREATE TRIGGER IF NOT EXISTS messages_ad AFTER DELETE ON messages BEGIN
    INSERT INTO messages_fts(messages_fts, rowid, content) VALUES('delete', old.rowid, old.content);
END;

CREATE TRIGGER IF NOT EXISTS messages_au AFTER UPDATE ON messages BEGIN
    INSERT INTO messages_fts(messages_fts, rowid, content) VALUES('delete', old.rowid, old.content);
    INSERT INTO messages_fts(rowid, content) VALUES (new.rowid, new.content);
END;

-- Promotion candidates for memory system
CREATE TABLE IF NOT EXISTS promotion_candidates (
    id TEXT PRIMARY KEY,
    group_folder TEXT NOT NULL,
    text TEXT NOT NULL,
    confidence REAL NOT NULL,
    evidence_count INTEGER NOT NULL,
    status TEXT NOT NULL DEFAULT 'PENDING',
    source TEXT,
    created_at TEXT NOT NULL
);

-- Provider usage tracking for cost analysis
CREATE TABLE IF NOT EXISTS provider_usage (
    id TEXT PRIMARY KEY,
    provider TEXT NOT NULL,
    model TEXT NOT NULL,
    input_tokens INTEGER NOT NULL,
    output_tokens INTEGER NOT NULL,
    cost_usd REAL,
    latency_ms INTEGER,
    timestamp TEXT NOT NULL
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

CREATE TABLE IF NOT EXISTS pipeline_states (
    id TEXT PRIMARY KEY,
    candidate_hash TEXT NOT NULL UNIQUE,
    state TEXT NOT NULL DEFAULT 'PROPOSED',
    source TEXT,
    content TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS goals (
    id TEXT PRIMARY KEY,
    agent_id TEXT NOT NULL,
    title TEXT NOT NULL,
    description TEXT,
    priority INTEGER NOT NULL DEFAULT 0,
    status TEXT NOT NULL DEFAULT 'ACTIVE',
    created_at TEXT NOT NULL,
    deadline TEXT,
    completed_at TEXT
);

-- Skill evolution records for persistence across restarts
CREATE TABLE IF NOT EXISTS skill_evolution (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    skill_name TEXT NOT NULL,
    user_message TEXT NOT NULL,
    helped INTEGER NOT NULL,
    confidence_delta REAL NOT NULL,
    recorded_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_skill_evolution_name ON skill_evolution(skill_name);
