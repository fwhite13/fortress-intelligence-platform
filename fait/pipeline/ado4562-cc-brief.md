# CC Brief: ADO4562 — Memory Import: Parse structured categories into separate topics

## File to Modify
`/home/fredw/projects/fip/fait/agent-harness/harness-server.js`

## Context
The `/import-memory` POST endpoint currently handles all imports as a single blob written to the `imported-memory` topic. The upstream export prompt (ADO#4560) now produces a structured 5-section document using `## N. CATEGORY` headers. This endpoint must detect that structure and split into separate memory topics.

## Current Handler (lines 1301–1360)
```javascript
// ─── import-memory endpoint (ADO#4053) ───────────────────────────────────────
app.post('/import-memory', async (req, res) => {
    const { userId, content } = req.body || {};
    if (!userId) return res.status(400).json({ error: 'userId required' });
    if (!content || !content.trim()) return res.status(400).json({ error: 'content required' });

    const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    if (!GUID_RE.test(userId)) {
        return res.status(400).json({ error: 'Invalid userId' });
    }

    const MAX_CONTENT_CHARS = 50_000;
    if (content.length > MAX_CONTENT_CHARS) {
        return res.status(400).json({ error: `Content too large (max ${MAX_CONTENT_CHARS} chars)` });
    }

    const internalToken = process.env.INTERNAL_API_TOKEN || '';

    // Calculate chunk count
    const CHUNK_SIZE = 500, OVERLAP = 50;
    let chunkCount = 0;
    for (let i = 0; i < content.length; i += CHUNK_SIZE - OVERLAP) {
        chunkCount++;
        if (i + CHUNK_SIZE >= content.length) break;
    }

    try {
        // Write to S3 + DB via Blazor API
        const resp = await fetch(`${FAIT_BASE_URL}/api/memory/write`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                ...(internalToken ? { 'X-Internal-Token': internalToken } : {}),
            },
            body: JSON.stringify({ userId, slug: 'imported-memory', title: 'Imported Memory', content }),
        });
        if (!resp.ok) {
            const text = await resp.text();
            const isHtml = text.trim().startsWith('<') || text.includes('<!DOCTYPE');
            const safeText = isHtml ? `[non-JSON response, HTTP ${resp.status}]` : text.substring(0, 200);
            throw new Error(`memory/write failed (${resp.status}): ${safeText}`);
        }

        // Upsert into pgvector (non-fatal — S3 write already succeeded)
        let pgvectorWarning = null;
        try {
            await upsertMemoryChunks(userId, 'memory/imported-memory.md', content);
        } catch (pgErr) {
            console.error('[harness] import-memory pgvector upsert failed (non-fatal):', pgErr.message);
            pgvectorWarning = pgErr.message;
        }

        const result = { success: true, chunks: chunkCount };
        if (pgvectorWarning) result.pgvectorWarning = pgvectorWarning;
        res.json(result);
    } catch (err) {
        console.error('[harness] import-memory error:', err.message);
        res.json({ success: false, error: err.message });
    }
});
```

## Required Changes

Replace the entire `/import-memory` handler (the `app.post('/import-memory', ...)` block) with the updated version below. Keep the comment header line `// ─── import-memory endpoint (ADO#4053) ───────────────────────────────────────` and replace the handler body with this new implementation:

```javascript
// ─── import-memory endpoint (ADO#4053) ───────────────────────────────────────
app.post('/import-memory', async (req, res) => {
    const { userId, content } = req.body || {};
    if (!userId) return res.status(400).json({ error: 'userId required' });
    if (!content || !content.trim()) return res.status(400).json({ error: 'content required' });

    const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    if (!GUID_RE.test(userId)) {
        return res.status(400).json({ error: 'Invalid userId' });
    }

    const MAX_CONTENT_CHARS = 50_000;
    if (content.length > MAX_CONTENT_CHARS) {
        return res.status(400).json({ error: `Content too large (max ${MAX_CONTENT_CHARS} chars)` });
    }

    const internalToken = process.env.INTERNAL_API_TOKEN || '';

    // ── Step 1: Strip wrapper formats ────────────────────────────────────────
    let parsed = content.trim();
    // Strip backtick code fences (``` ... ```)
    if (parsed.startsWith('```')) {
        const firstNewline = parsed.indexOf('\n');
        const lastFence = parsed.lastIndexOf('```');
        if (firstNewline !== -1 && lastFence > firstNewline) {
            parsed = parsed.slice(firstNewline + 1, lastFence).trim();
        }
    }
    // Strip ===== banner lines (some AI tools wrap exports this way)
    parsed = parsed.replace(/^={5,}.*$/gm, '').trim();

    // ── Step 2: Parse sections by ## N. CATEGORY headers ─────────────────────
    const sectionRegex = /^##\s+\d+\.\s+(.+)/gm;
    const sections = [];
    let match;
    let lastIndex = 0;
    let lastTitle = null;

    while ((match = sectionRegex.exec(parsed)) !== null) {
        if (lastTitle !== null) {
            sections.push({ title: lastTitle, body: parsed.slice(lastIndex, match.index).trim() });
        }
        lastTitle = match[1].trim().toUpperCase();
        lastIndex = match.index + match[0].length;
    }
    if (lastTitle !== null) {
        sections.push({ title: lastTitle, body: parsed.slice(lastIndex).trim() });
    }

    // ── Step 3: Slug/title maps ───────────────────────────────────────────────
    const SLUG_MAP = {
        'INSTRUCTIONS': 'imported-instructions',
        'IDENTITY':     'imported-identity',
        'CAREER':       'imported-career',
        'PROJECTS':     'imported-projects',
        'PREFERENCES':  'imported-preferences',
    };
    const TITLE_MAP = {
        'INSTRUCTIONS': 'Imported Instructions',
        'IDENTITY':     'Imported Identity',
        'CAREER':       'Imported Career',
        'PROJECTS':     'Imported Projects',
        'PREFERENCES':  'Imported Preferences',
    };

    // Helper: calculate chunk count for a given string
    const CHUNK_SIZE = 500, OVERLAP = 50;
    function countChunks(text) {
        let count = 0;
        for (let i = 0; i < text.length; i += CHUNK_SIZE - OVERLAP) {
            count++;
            if (i + CHUNK_SIZE >= text.length) break;
        }
        return count;
    }

    // Helper: write one section via Blazor API + upsert pgvector
    async function writeSection(slug, title, body) {
        const resp = await fetch(`${FAIT_BASE_URL}/api/memory/write`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                ...(internalToken ? { 'X-Internal-Token': internalToken } : {}),
            },
            body: JSON.stringify({ userId, slug, title, content: body }),
        });
        if (!resp.ok) {
            const text = await resp.text();
            const isHtml = text.trim().startsWith('<') || text.includes('<!DOCTYPE');
            const safeText = isHtml ? `[non-JSON response, HTTP ${resp.status}]` : text.substring(0, 200);
            throw new Error(`memory/write failed for "${slug}" (${resp.status}): ${safeText}`);
        }

        let pgvectorWarning = null;
        try {
            await upsertMemoryChunks(userId, `memory/${slug}.md`, body);
        } catch (pgErr) {
            console.error(`[harness] import-memory pgvector upsert failed for "${slug}" (non-fatal):`, pgErr.message);
            pgvectorWarning = pgErr.message;
        }

        return { chunks: countChunks(body), pgvectorWarning };
    }

    try {
        // ── Step 4: Structured path — write each section separately ──────────
        if (sections.length > 0) {
            let totalChunks = 0;
            const warnings = [];

            for (const section of sections) {
                const slug = SLUG_MAP[section.title] || `imported-${section.title.toLowerCase().replace(/[^a-z0-9]+/g, '-')}`;
                const title = TITLE_MAP[section.title] || `Imported ${section.title.charAt(0) + section.title.slice(1).toLowerCase()}`;
                const { chunks, pgvectorWarning } = await writeSection(slug, title, section.body);
                totalChunks += chunks;
                if (pgvectorWarning) warnings.push(`${slug}: ${pgvectorWarning}`);
            }

            const result = { success: true, chunks: totalChunks, sections: sections.length };
            if (warnings.length > 0) result.pgvectorWarning = warnings.join('; ');
            return res.json(result);
        }

        // ── Step 5: Fallback — unstructured paste (backward compat) ──────────
        const resp = await fetch(`${FAIT_BASE_URL}/api/memory/write`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                ...(internalToken ? { 'X-Internal-Token': internalToken } : {}),
            },
            body: JSON.stringify({ userId, slug: 'imported-memory', title: 'Imported Memory', content: parsed }),
        });
        if (!resp.ok) {
            const text = await resp.text();
            const isHtml = text.trim().startsWith('<') || text.includes('<!DOCTYPE');
            const safeText = isHtml ? `[non-JSON response, HTTP ${resp.status}]` : text.substring(0, 200);
            throw new Error(`memory/write failed (${resp.status}): ${safeText}`);
        }

        let pgvectorWarning = null;
        try {
            await upsertMemoryChunks(userId, 'memory/imported-memory.md', parsed);
        } catch (pgErr) {
            console.error('[harness] import-memory pgvector upsert failed (non-fatal):', pgErr.message);
            pgvectorWarning = pgErr.message;
        }

        const result = { success: true, chunks: countChunks(parsed) };
        if (pgvectorWarning) result.pgvectorWarning = pgvectorWarning;
        res.json(result);
    } catch (err) {
        console.error('[harness] import-memory error:', err.message);
        res.json({ success: false, error: err.message });
    }
});
```

## Constraints
- ONLY modify the `/import-memory` handler block (from `// ─── import-memory endpoint (ADO#4053)` through the closing `});`)
- Do NOT touch any other handlers or code
- Do NOT change the function signature, validation logic (userId, content, GUID_RE, MAX_CONTENT_CHARS), or surrounding structure
- The `FAIT_BASE_URL` and `upsertMemoryChunks` variables/functions are already in scope — do not redefine them
- Preserve the existing `internalToken` logic exactly as-is
- The `content` variable from `req.body` is the raw input; `parsed` is the stripped version used for all writes

## Acceptance Criteria
1. Structured input with `## N. CATEGORY` headers splits into separate memory topics
2. Code block fences stripped before parsing
3. `=====` banner lines stripped before parsing
4. Each section calls POST /api/memory/write and upsertMemoryChunks with `sourceFile: 'memory/imported-{slug}.md'`
5. Unstructured paste (no `## N.` headers) falls back to single `imported-memory` topic
6. Return JSON includes `{ success: true, chunks: <total>, sections: <count> }` for structured path
7. Return JSON for fallback is `{ success: true, chunks: <count> }` (no `sections` field) — same as before
8. No syntax errors; no regression on existing behavior

## Output
After making the change, output: "ADO4562 DONE" and nothing else.
