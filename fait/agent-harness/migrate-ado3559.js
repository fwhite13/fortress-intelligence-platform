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

    // 2. Add folder_id to workspace_files (Aurora MySQL 8.0.40 does NOT support IF NOT EXISTS on ADD COLUMN)
    await conn.execute(`
        SET @col_exists = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'workspace_files' AND COLUMN_NAME = 'folder_id')
    `);
    await conn.execute(`
        SET @sql = IF(@col_exists = 0,
            'ALTER TABLE workspace_files ADD COLUMN folder_id CHAR(36) NULL AFTER user_id',
            'SELECT 1')
    `);
    await conn.execute('PREPARE stmt FROM @sql');
    await conn.execute('EXECUTE stmt');
    await conn.execute('DEALLOCATE PREPARE stmt');
    console.log('[migrate-3559] workspace_files.folder_id column: OK');

    // 3. Add last_task_folder_id to users
    await conn.execute(`
        SET @col_exists = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'users' AND COLUMN_NAME = 'last_task_folder_id')
    `);
    await conn.execute(`
        SET @sql = IF(@col_exists = 0,
            'ALTER TABLE users ADD COLUMN last_task_folder_id CHAR(36) NULL',
            'SELECT 1')
    `);
    await conn.execute('PREPARE stmt FROM @sql');
    await conn.execute('EXECUTE stmt');
    await conn.execute('DEALLOCATE PREPARE stmt');
    console.log('[migrate-3559] users.last_task_folder_id column: OK');

    // Verify
    const [folders] = await conn.execute("SHOW TABLES LIKE 'workspace_folders'");
    console.log('[migrate-3559] workspace_folders exists:', folders.length > 0);
    const [wfCols] = await conn.execute("SHOW COLUMNS FROM workspace_files LIKE 'folder_id'");
    console.log('[migrate-3559] workspace_files.folder_id exists:', wfCols.length > 0);
    const [userCols] = await conn.execute("SHOW COLUMNS FROM users LIKE 'last_task_folder_id'");
    console.log('[migrate-3559] users.last_task_folder_id exists:', userCols.length > 0);

    await conn.end();
    console.log('[migrate-3559] migration complete');
}

migrate().catch(err => {
    console.error('[migrate-3559] FAILED:', err.message);
    process.exit(1);
});
