-- ADO#3126 — Fargate Session Lifecycle (Backend) Migration SQL
-- Source: DatabaseInitializationService.cs (extraTables + alterStatements)
-- ALL statements are fully idempotent (CREATE IF NOT EXISTS / ADD COLUMN IF NOT EXISTS)
-- DO NOT run against fait_dev until Clint approves

-- ─── NEW TABLE: user_sessions ─────────────────────────────────────────────────
-- Tracks Fargate ECS task sessions per user. New table, zero impact on existing data.

CREATE TABLE IF NOT EXISTS user_sessions (
    id VARCHAR(36) NOT NULL PRIMARY KEY,
    user_id VARCHAR(36) NOT NULL,
    started_at DATETIME(6) NOT NULL,
    last_active_at DATETIME(6) NOT NULL,
    ended_at DATETIME(6) NULL,
    task_arn VARCHAR(500) NULL,
    private_ip VARCHAR(45) NULL,
    fargate_status VARCHAR(20) NULL,
    fargate_session_id VARCHAR(200) NULL,
    task_definition_revision VARCHAR(100) NULL,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    INDEX ix_user_sessions_user_id (user_id),
    INDEX ix_user_sessions_last_active_at (last_active_at)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

-- ─── ADDITIVE COLUMNS: users table ───────────────────────────────────────────
-- Needed for Story 3 (onboarding). Nullable columns — zero impact on existing rows.
-- MySQL 8+ ADD COLUMN IF NOT EXISTS syntax — fully idempotent.

ALTER TABLE users ADD COLUMN IF NOT EXISTS onboarding_completed_at DATETIME(6) NULL;
ALTER TABLE users ADD COLUMN IF NOT EXISTS onboarding_step INT NULL;
