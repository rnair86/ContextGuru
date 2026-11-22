CREATE TABLE contexts( id INTEGER PRIMARY KEY, project_key TEXT NOT NULL, project_path TEXT NOT NULL, project_name TEXT NOT NULL, name TEXT NOT NULL, summary TEXT NOT NULL, body TEXT NOT NULL, source_agent TEXT, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, UNIQUE(project_key, name) )
CREATE INDEX contexts_latest ON contexts(project_key, updated_at DESC, id DESC)
