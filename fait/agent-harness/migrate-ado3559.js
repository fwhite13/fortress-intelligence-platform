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

    console.log('[migrate-3559] connected to DB');

    // 1. Create workspace_folders table
    await conn.execute(`
        CREATE TABLE IF NOT EXISTS workspace_folders (
            id CHAR(36) NOT NULL,
            user_id CHAR(36) NOT NULL,
            name VARCHAR(64) NOT NULL,
            s3_prefix VARCHAR(500) NOT NULL,
            created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            last_used_at DATETIME(6) NULL,
            PRIMARY KEY (id),
            UNIQUE KEY uq_user_folder_name (user_id, name),
            KEY idx_user_id (user_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
    `);
    console.log('[migrate-3559] workspace_folders table: OK');

    // 2. Add folder_id to user_workspace_files (Aurora MySQL 8.0.40 does NOT support IF NOT EXISTS on ADD COLUMN)
    // Use INFORMATION_SCHEMA check instead
    const [wfCheck] = await conn.execute(
        `SELECT COUNT(*) as cnt FROM INFORMATION_SCHEMA.COLUMNS
         WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'user_workspace_files' AND COLUMN_NAME = 'folder_id'`
    );
    if (wfCheck[0].cnt === 0) {
        await conn.execute('ALTER TABLE user_workspace_files ADD COLUMN folder_id CHAR(36) NULL AFTER user_id');
        console.log('[migrate-3559] user_workspace_files.folder_id column: ADDED');
    } else {
        console.log('[migrate-3559] user_workspace_files.folder_id column: already exists, skipped');
    }

    // 3. Add last_task_folder_id to users
    const [userCheck] = await conn.execute(
        `SELECT COUNT(*) as cnt FROM INFORMATION_SCHEMA.COLUMNS
         WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'users' AND COLUMN_NAME = 'last_task_folder_id'`
    );
    if (userCheck[0].cnt === 0) {
        await conn.execute('ALTER TABLE users ADD COLUMN last_task_folder_id CHAR(36) NULL');
        console.log('[migrate-3559] users.last_task_folder_id column: ADDED');
    } else {
        console.log('[migrate-3559] users.last_task_folder_id column: already exists, skipped');
    }

    // Verify
    const [folders] = await conn.execute("SHOW TABLES LIKE 'workspace_folders'");
    console.log('[migrate-3559] workspace_folders exists:', folders.length > 0);

    const [wfCols] = await conn.execute("SHOW COLUMNS FROM user_workspace_files LIKE 'folder_id'");
    console.log('[migrate-3559] user_workspace_files.folder_id exists:', wfCols.length > 0);

    const [userCols] = await conn.execute("SHOW COLUMNS FROM users LIKE 'last_task_folder_id'");
    console.log('[migrate-3559] users.last_task_folder_id exists:', userCols.length > 0);

    await conn.end();
    console.log('[migrate-3559] migration complete');
}

migrate().catch(err => {
    console.error('[migrate-3559] FAILED:', err.message);
    process.exit(1);
});
