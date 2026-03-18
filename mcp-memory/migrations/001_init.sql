CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS cc_memory_users (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    username    VARCHAR(64) NOT NULL UNIQUE,
    email       VARCHAR(256) NOT NULL UNIQUE,
    api_token   VARCHAR(128) NOT NULL UNIQUE,
    scope       VARCHAR(20)  NOT NULL DEFAULT 'user',
    is_active   BOOLEAN      NOT NULL DEFAULT TRUE,
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    last_used_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_ccmu_token ON cc_memory_users (api_token);

CREATE TABLE IF NOT EXISTS cc_memory_entries (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     UUID        REFERENCES cc_memory_users(id) ON DELETE CASCADE,
    scope       VARCHAR(20) NOT NULL,
    project     VARCHAR(64),
    content     TEXT        NOT NULL,
    entry_type  VARCHAR(32) NOT NULL DEFAULT 'note',
    source      VARCHAR(32) NOT NULL DEFAULT 'manual',
    embedding   vector(1024),
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by  UUID        REFERENCES cc_memory_users(id),
    expires_at  TIMESTAMPTZ,
    metadata    JSONB       NOT NULL DEFAULT '{}'
);

CREATE INDEX IF NOT EXISTS idx_ccme_scope      ON cc_memory_entries (scope);
CREATE INDEX IF NOT EXISTS idx_ccme_project    ON cc_memory_entries (project);
CREATE INDEX IF NOT EXISTS idx_ccme_user       ON cc_memory_entries (user_id);
CREATE INDEX IF NOT EXISTS idx_ccme_created_at ON cc_memory_entries (created_at DESC);
CREATE INDEX IF NOT EXISTS idx_ccme_embedding  ON cc_memory_entries
    USING ivfflat (embedding vector_cosine_ops) WITH (lists = 50);
