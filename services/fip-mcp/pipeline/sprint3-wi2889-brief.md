# BUILD BRIEF — ADO#2889 — MS365 MCP Connector
**Sprint 3, Lane 2 | FAIT v2 Epic #2835 | §7.0, §7.3**
**Agent:** Tony Stark | **Cycle:** 1 | **Date:** 2026-05-07

---

## Context

You are Tony Stark (software-engineer). You are implementing FAIT v2 Sprint 3, WI #2889.
**Repo:** `~/projects/fip/services/fip-mcp/` | **Branch:** `main`
**Spec:** `memory/projects/fait-v2-spec-2026-04-27.md` (§7.0, §7.3)

---

## What's Already Built (on main)

`fip-mcp` is a live ECS service running the MCP SDK with forge-kb tools:
- `src/server.js` — Express + MCP SDK, Entra JWT auth, SSE + StreamableHTTP transports
- `src/auth.js` — validates Entra Bearer token, extracts `oid`, `groups`, `tid`
- `src/tools/` — `search_kb.js`, `list_kbs.js`, `add_to_kb.js`, `get_kb_metadata.js`, etc.
- `package.json` — ESM, Node 22, `@modelcontextprotocol/sdk ^1.11.0`, `express ^4.18.0`

The server uses a **factory pattern**: `createMcpServer(user)` creates a new `McpServer` instance per request with the user's claims in closure. Each tool calls the user context from closure — no metadata injection needed.

---

## Objective

Add the `ms365` tool group to fip-mcp. These tools proxy Microsoft Graph API calls on behalf of authenticated users — email, calendar, Teams. The caller's Entra Bearer token is passed to fip-mcp; fip-mcp uses it to call Graph directly (the token already has MS Graph delegated scopes from FAIT's Entra app registration).

**Key point:** No per-user token storage needed for MS365 — the Entra Bearer token the caller passes to fip-mcp IS the Graph token (same tenant, delegated scopes). fip-mcp simply uses it for Graph calls.

---

## What to Build

### 1. Install `@microsoft/microsoft-graph-client`

Add to `package.json`:
```json
"@microsoft/microsoft-graph-client": "^3.0.0",
"isomorphic-fetch": "^3.0.0"
```

Run `npm install` after adding.

### 2. `src/tools/ms365/` directory with Graph helpers

Create `src/tools/ms365/graph-client.js`:
```javascript
import 'isomorphic-fetch';
import { Client } from '@microsoft/microsoft-graph-client';

/**
 * Creates an authenticated Graph client using the caller's Bearer token.
 * The token is passed directly — no OAuth exchange needed.
 */
export function createGraphClient(accessToken) {
    return Client.init({
        authProvider: (done) => done(null, accessToken),
    });
}
```

### 3. MS365 Tool Files

Create the following tools in `src/tools/ms365/`:

#### `list_emails.js`
```javascript
export async function listEmails(graphClient, { top = 10, filter = null, search = null }) {
    // GET /me/messages
    // Params: $top, $filter (OData), $search, $select=id,subject,from,receivedDateTime,bodyPreview,isRead
    // Return: array of { id, subject, from, receivedDateTime, bodyPreview, isRead }
}
```

#### `get_email.js`
```javascript
export async function getEmail(graphClient, { messageId }) {
    // GET /me/messages/{messageId}
    // Return: full message with body.content
}
```

#### `send_email.js`
```javascript
export async function sendEmail(graphClient, { to, subject, body, cc = [] }) {
    // POST /me/sendMail
    // Body: { message: { subject, body: { contentType: 'HTML', content: body }, toRecipients, ccRecipients } }
}
```

#### `list_calendar_events.js`
```javascript
export async function listCalendarEvents(graphClient, { startDateTime, endDateTime, top = 10 }) {
    // GET /me/calendarView?startDateTime=...&endDateTime=...
    // Return: array of { id, subject, start, end, location, organizer, attendees, isOnlineMeeting }
}
```

#### `create_calendar_event.js`
```javascript
export async function createCalendarEvent(graphClient, { subject, start, end, attendees = [], body = '', location = '', isTeamsMeeting = false }) {
    // POST /me/events
    // If isTeamsMeeting: add onlineMeetingProvider: 'teamsForBusiness'
    // Return: { id, subject, start, end, onlineMeeting (if applicable) }
}
```

#### `list_teams_chats.js`
```javascript
export async function listTeamsChats(graphClient, { top = 10 }) {
    // GET /me/chats?$expand=members&$top={top}
    // Return: array of { id, chatType, topic, members }
}
```

#### `send_teams_message.js`
```javascript
export async function sendTeamsMessage(graphClient, { chatId, content }) {
    // POST /me/chats/{chatId}/messages
    // Body: { body: { content, contentType: 'html' } }
}
```

### 4. Register MS365 Tools in `src/server.js`

In `createMcpServer(user)`, after the existing forge-kb tools, add MS365 tools:

```javascript
// Extract Bearer token from request context for Graph calls
// The user object has: oid, groups, tid — but we need the raw token for Graph
// Pass rawToken through the user closure (update createMcpServer signature)
```

**Important:** The Graph client needs the raw Bearer token, not just the decoded claims. Update `createMcpServer` to accept `(user, rawToken)` and pass `rawToken` to Graph tool implementations.

The auth middleware already has the raw token (it calls `validateToken(authHeader)` which has `token = authHeader.slice(7)`). Pass it through to `createMcpServer`.

Tool registrations follow the existing pattern:
```javascript
server.tool(
    'list_emails',
    'List emails from the user\'s inbox via Microsoft Graph.',
    {
        top: z.number().int().min(1).max(50).optional().default(10).describe('Max emails to return'),
        filter: z.string().optional().describe('OData filter expression'),
        search: z.string().optional().describe('Search query string'),
    },
    async ({ top, filter, search }) => {
        try {
            const client = createGraphClient(rawToken);
            const result = await listEmails(client, { top, filter, search });
            return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
        } catch (err) {
            const e = handleToolError(err);
            return { content: [{ type: 'text', text: JSON.stringify(e, null, 2) }], isError: true };
        }
    }
);
// ... same pattern for get_email, send_email, list_calendar_events, create_calendar_event, list_teams_chats, send_teams_message
```

### 5. Error Handling

Graph API errors have a specific shape. Add to `src/utils/graph-error.js`:
```javascript
export function handleGraphError(err) {
    if (err.statusCode) {
        return { error: { code: `GRAPH_${err.statusCode}`, message: err.message } };
    }
    return { error: { code: 'GRAPH_ERROR', message: err.message ?? 'Microsoft Graph error' } };
}
```

Use `handleGraphError` (not `handleToolError`) for Graph tool catch blocks.

### 6. Update `src/server.js` imports

Add:
```javascript
import { createGraphClient } from './tools/ms365/graph-client.js';
import { listEmails } from './tools/ms365/list_emails.js';
import { getEmail } from './tools/ms365/get_email.js';
import { sendEmail } from './tools/ms365/send_email.js';
import { listCalendarEvents } from './tools/ms365/list_calendar_events.js';
import { createCalendarEvent } from './tools/ms365/create_calendar_event.js';
import { listTeamsChats } from './tools/ms365/list_teams_chats.js';
import { sendTeamsMessage } from './tools/ms365/send_teams_message.js';
import { handleGraphError } from './utils/graph-error.js';
```

### 7. Acceptance Criteria
- [ ] `list_emails` tool callable and returns inbox emails via Graph `/me/messages`
- [ ] `get_email` tool returns full email body
- [ ] `send_email` tool posts to `/me/sendMail` (no actual send test needed — just correct Graph call)
- [ ] `list_calendar_events` returns events from `/me/calendarView`
- [ ] `create_calendar_event` posts to `/me/events` with Teams meeting support
- [ ] `list_teams_chats` returns from `/me/chats`
- [ ] `send_teams_message` posts to `/me/chats/{chatId}/messages`
- [ ] All 7 tools registered in `createMcpServer` factory
- [ ] `rawToken` threaded through to Graph client
- [ ] Graph errors use `handleGraphError`, not `handleToolError`
- [ ] `node src/server.js` starts cleanly (smoke test with `node --input-type=module <<< "import './src/server.js'"` or equivalent)
- [ ] CC CLI used (mandatory)

---

## Mandatory Rules
- **CC CLI MANDATORY:**
  ```bash
  CLAUDE_CODE_ENTRYPOINT=ado-pipeline CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
  cat brief.md | claude --model sonnet --print --dangerously-skip-permissions
  ```
- Work dir: `~/projects/fip/services/fip-mcp/`
- ESM throughout — all files use `import`/`export`, no `require()`
- Commit: `feat(fip-mcp#2889): MS365 MCP connector (email, calendar, Teams)`
- No hardcoded tenant IDs, client IDs, or secrets — use env vars
- Do NOT break existing forge-kb tools

---

## ADO Comment (MANDATORY)
```bash
mcporter call devops.add_comment --args '{"project":"Fortress","id":2889,"text":"**[Tony Stark — BUILD cycle 1]**\nCommit {hash}: MS365 MCP connector — email, calendar, Teams tools added to fip-mcp. Build: SUCCEEDED."}'
```

---

## Deliverables
1. Build Report at `~/projects/fip/services/fip-mcp/pipeline/ADO2889-BUILD-REPORT.md`
2. All changes committed and pushed to `origin/main`
3. ADO WI #2889 comment
4. Report back to Maria
