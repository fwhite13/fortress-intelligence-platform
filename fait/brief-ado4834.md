# CC Task: ADO#4834 — Supporting/Intermediary Files Saved to Working Folder

## Working directory
`/home/fredw/projects/fip/fait/`

## Files to modify
- `agent-harness/harness-server.js` only

---

## Context

The post-task sync already exists and works. It:
1. Takes `preSyncSnapshot` before task starts (line ~3696-3697)
2. After task, calls `findDirtyFiles(preSyncSnapshot, postSyncSnapshot)` to get new/changed files
3. Uploads all dirty files to S3 with provenance records
4. Emits `files_updated` SSE event with all uploaded files

The Blazor `file-summary-block` already renders ALL files from the `files_updated` event.

The problem: `buildLocalSnapshot` walks ALL files including Python cache, CC internals, git data, etc. These get uploaded as dirty files when they're modified during the task. We need to filter them out.

---

## Changes Required

### 1. Fix `buildLocalSnapshot` to exclude system/cache directories and file patterns

In `harness-server.js`, find the `buildLocalSnapshot` function (around line 2717):

```javascript
function buildLocalSnapshot(dir) {
    // Returns Map<relativePath, {size, mtime}> for all files in dir (recursive)
    const result = new Map();
    if (!fs.existsSync(dir)) return result;
    function walk(current, base) {
        for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
            const full = path.join(current, entry.name);
            const rel = path.relative(base, full);
            if (entry.isDirectory()) walk(full, base);
            else {
                const stat = fs.statSync(full);
                result.set(rel, { size: stat.size, mtime: stat.mtimeMs });
            }
        }
    }
    walk(dir, dir);
    return result;
}
```

Replace the `walk` function inside `buildLocalSnapshot` to skip excluded directories and files:

```javascript
function buildLocalSnapshot(dir) {
    // Returns Map<relativePath, {size, mtime}> for all files in dir (recursive)
    // Excludes system/cache dirs and files that should not be synced to workspace
    const EXCLUDED_DIRS = new Set([
        '__pycache__', '.claude', '.git', '.svn', 'node_modules',
        '.pytest_cache', '.mypy_cache', '.ruff_cache', '.venv', 'venv', 'env',
        'dist', 'build', '.tox', '.eggs', '*.egg-info'
    ]);
    const result = new Map();
    if (!fs.existsSync(dir)) return result;
    function walk(current, base) {
        for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
            const full = path.join(current, entry.name);
            const rel = path.relative(base, full);
            if (entry.isDirectory()) {
                // Skip excluded directories
                if (EXCLUDED_DIRS.has(entry.name)) continue;
                walk(full, base);
            } else {
                // Skip excluded file patterns
                if (entry.name.endsWith('.pyc')) continue;
                if (entry.name.endsWith('.pyo')) continue;
                if (entry.name.endsWith('.pyd')) continue;
                if (entry.name === '.DS_Store') continue;
                if (entry.name.endsWith('~')) continue;
                if (entry.name.startsWith('.#')) continue; // emacs temp files
                const stat = fs.statSync(full);
                result.set(rel, { size: stat.size, mtime: stat.mtimeMs });
            }
        }
    }
    walk(dir, dir);
    return result;
}
```

### 2. Add exclusion logging to post-sync upload loop

In the post-task sync loop (around line 3931 where `for (const relPath of dirtyFiles)`), the current code has:
```javascript
if (localPath.startsWith(`${WORKSPACE_DIR}/${userId}/readonly/`)) continue;
```

Add a size guard to skip very large files (> 100MB) that are unlikely to be intentional outputs:
```javascript
if (localPath.startsWith(`${WORKSPACE_DIR}/${userId}/readonly/`)) continue;
// Skip binary cache/system files that slipped through snapshot exclusions
const fileSizeBytes = (() => { try { return fs.statSync(localPath).size; } catch { return null; } })();
if (fileSizeBytes !== null && fileSizeBytes > 100 * 1024 * 1024) {
    console.warn(`[harness] post-sync skipping large file (>100MB): ${relPath} size=${fileSizeBytes} userId=${userId}`);
    continue;
}
```

### 3. Verify file-summary-block already shows all files

The Blazor `files_updated` handler already sets `_currentFileSummary = payload` which renders ALL files from the event in the file summary block. No Blazor changes needed.

---

## Acceptance Criteria
- AC1: After CC task completes, all new files in working directory are synced to workspace
- AC2: Python cache files and CC internals (`.claude/`, `*.pyc`, `__pycache__/`) are excluded from sync
- AC3: Chat file summary block lists all created/modified files (already works via existing files_updated handler)
- AC4: Pre-task snapshot is taken and used for dirty-file detection (already works)

---

## Final steps
1. There is no .NET build for harness changes — skip dotnet build
2. Commit: `cd /home/fredw/projects/fip && git add -A && git commit -m "ADO#4834: exclude __pycache__, .pyc, .claude/, .git from post-task file sync; add 100MB size guard in upload loop"`
