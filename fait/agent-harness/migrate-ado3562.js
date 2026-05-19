'use strict';
const mysql = require('mysql2/promise');

async function migrate() {
    const conn = await mysql.createConnection({
        host: process.env.FORTRESS_DB_HOST || 'localhost',
        port: parseInt(process.env.FORTRESS_DB_PORT || '3306', 10),
        database: process.env.DB_NAME || 'fait',
        user: process.env.FORTRESS_DB_USER || 'fait',
        password: process.env.FORTRESS_DB_PASS || '',
        ssl: process.env.DB_SSL !== 'false' ? { rejectUnauthorized: false } : false,
        connectTimeout: 10000,
        multipleStatements: false,
    });

    console.log('[migrate-3562] connected to DB');

    // 1. Create workspace_file_versions table (idempotent)
    await conn.execute(`
        CREATE TABLE IF NOT EXISTS workspace_file_versions (
            id CHAR(36) NOT NULL,
            file_id CHAR(36) NOT NULL,
            version_number INT NOT NULL,
            s3_key VARCHAR(500) NOT NULL,
            size BIGINT NULL,
            size_bytes BIGINT NULL,
            created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            created_by ENUM('user','assistant','cc') NOT NULL,
            conversation_id CHAR(36) NULL,
            turn_index INT NULL,
            PRIMARY KEY (id),
            KEY idx_file_id (file_id),
            KEY idx_conversation_id (conversation_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
    `);
    console.log('[migrate-3562] workspace_file_versions table: OK');

    // 2. Add conversation_id to workspace_file_versions (INFORMATION_SCHEMA guard)
    const [wfvConvCheck] = await conn.execute(
        `SELECT COUNT(*) as cnt FROM INFORMATION_SCHEMA.COLUMNS
         WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'workspace_file_versions' AND COLUMN_NAME = 'conversation_id'`
    );
    if (wfvConvCheck[0].cnt === 0) {
        await conn.execute('ALTER TABLE workspace_file_versions ADD COLUMN conversation_id CHAR(36) NULL');
        console.log('[migrate-3562] workspace_file_versions.conversation_id column: ADDED');
    } else {
        console.log('[migrate-3562] workspace_file_versions.conversation_id column: already exists, skipped');
    }

    // 3. Add turn_index to workspace_file_versions (INFORMATION_SCHEMA guard)
    const [wfvTurnCheck] = await conn.execute(
        `SELECT COUNT(*) as cnt FROM INFORMATION_SCHEMA.COLUMNS
         WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'workspace_file_versions' AND COLUMN_NAME = 'turn_index'`
    );
    if (wfvTurnCheck[0].cnt === 0) {
        await conn.execute('ALTER TABLE workspace_file_versions ADD COLUMN turn_index INT NULL');
        console.log('[migrate-3562] workspace_file_versions.turn_index column: ADDED');
    } else {
        console.log('[migrate-3562] workspace_file_versions.turn_index column: already exists, skipped');
    }

    // 4. Add conversation_id to user_workspace_uploads (INFORMATION_SCHEMA guard)
    const [uwuConvCheck] = await conn.execute(
        `SELECT COUNT(*) as cnt FROM INFORMATION_SCHEMA.COLUMNS
         WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'user_workspace_uploads' AND COLUMN_NAME = 'conversation_id'`
    );
    if (uwuConvCheck[0].cnt === 0) {
        await conn.execute('ALTER TABLE user_workspace_uploads ADD COLUMN conversation_id CHAR(36) NULL');
        console.log('[migrate-3562] user_workspace_uploads.conversation_id column: ADDED');
    } else {
        console.log('[migrate-3562] user_workspace_uploads.conversation_id column: already exists, skipped');
    }

    // Verify
    const [wfvTable] = await conn.execute("SHOW TABLES LIKE 'workspace_file_versions'");
    console.log('[migrate-3562] workspace_file_versions exists:', wfvTable.length > 0);

    const [wfvConvCol] = await conn.execute("SHOW COLUMNS FROM workspace_file_versions LIKE 'conversation_id'");
    console.log('[migrate-3562] workspace_file_versions.conversation_id exists:', wfvConvCol.length > 0);

    const [wfvTurnCol] = await conn.execute("SHOW COLUMNS FROM workspace_file_versions LIKE 'turn_index'");
    console.log('[migrate-3562] workspace_file_versions.turn_index exists:', wfvTurnCol.length > 0);

    const [uwuConvCol] = await conn.execute("SHOW COLUMNS FROM user_workspace_uploads LIKE 'conversation_id'");
    console.log('[migrate-3562] user_workspace_uploads.conversation_id exists:', uwuConvCol.length > 0);

    await conn.end();
    console.log('[migrate-3562] migration complete');
}

migrate().catch(err => {
    console.error('[migrate-3562] FAILED:', err.message);
    process.exit(1);
});
