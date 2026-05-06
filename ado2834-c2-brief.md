# ADO#2834 Cycle 2 — Fix list_kb_files personal KB prefix (Entra OID → FAIT GUID)

## Problem
`list_kb_files.js` uses `user.user_id` (Entra OID) for the personal KB S3 prefix, but FAIT uploads
to `kb-docs/personal/{AppUser.Id}/` (FAIT internal GUID). They're different UUIDs. Tool returns 0 files.

## Files to change — exactly 3, no others

---

### FILE 1: `fait/src/FortressAI.Web/Controllers/FirmIntegrationController.cs`

In the `ResolveUser` method (HttpGet "resolve-user"), after the X-Firm-Secret auth check and the
`if (string.IsNullOrWhiteSpace(entraOid))` guard, replace the entire block of flawed lookup code
(from `await using var db = ...` through `return Ok(...)`) with this exact implementation:

```csharp
await using var db = await _dbFactory.CreateDbContextAsync();

var user = await db.Users
    .Where(u => u.EntraOid == entraOid && u.IsActive)
    .FirstOrDefaultAsync();

if (user == null)
{
    var entraUsers = await db.Users
        .Where(u => u.IsEntraUser && u.IsActive)
        .ToListAsync();
    user = entraUsers.Count == 1 ? entraUsers[0] : null;
}

if (user == null)
    return NotFound(new { error = "No matching FAIT user found for Entra OID" });

_logger.LogInformation("FirmIntegration: Resolved entraOid {OID} → FAIT user {UserId}", entraOid, user.Id);
return Ok(new { userId = user.Id.ToString() });
```

The current flawed code to replace starts just after the `if (string.IsNullOrWhiteSpace(entraOid))` guard
and contains a large block of comments ending with:
```
        var user = await db.Users
            .Where(u => u.IsEntraUser && u.IsActive)
            .OrderByDescending(u => u.CreatedAt)
            .FirstOrDefaultAsync();

        if (user == null)
            return NotFound(new { error = "No matching FAIT user found for this Entra OID" });

        _logger.LogInformation("FirmIntegration: Resolved entraOid {OID} → FAIT user {UserId}", entraOid, user.Id);
        return Ok(new { userId = user.Id.ToString() });
```

Replace that entire section (the `await using var db` through the final `return Ok(...)`) with the new code above.

DO NOT change anything else in this file — not the other methods, not the imports, not the class definition.

---

### FILE 2: CREATE NEW `services/fip-mcp/src/utils/fait-user-resolver.js`

Create this file with exactly this content:

```javascript
const FAIT_BASE_URL = process.env.FAIT_BASE_URL ?? 'https://fait.fortressam.ai';
const FAIT_INTERNAL_SECRET = process.env.FAIT_INTERNAL_SECRET;

export async function getFaitUserId(entraOid) {
  if (!FAIT_INTERNAL_SECRET) {
    console.warn('[fip-mcp] FAIT_INTERNAL_SECRET not set — cannot resolve FAIT user ID');
    return null;
  }
  try {
    const url = `${FAIT_BASE_URL}/api/firm/resolve-user?entraOid=${encodeURIComponent(entraOid)}`;
    const resp = await fetch(url, {
      headers: { 'X-Firm-Secret': FAIT_INTERNAL_SECRET },
      signal: AbortSignal.timeout(5000),
    });
    if (!resp.ok) {
      console.warn(`[fip-mcp] resolve-user returned ${resp.status} for OID ${entraOid}`);
      return null;
    }
    const data = await resp.json();
    return data.userId ?? null;
  } catch (e) {
    console.warn(`[fip-mcp] resolve-user failed: ${e.message}`);
    return null;
  }
}
```

---

### FILE 3: MODIFY `services/fip-mcp/src/tools/list_kb_files.js`

Make these changes:

1. Add import at the top of the file (after the existing imports):
   ```javascript
   import { getFaitUserId } from '../utils/fait-user-resolver.js';
   ```

2. In `getS3Prefix`, update the PERSONAL case from:
   ```javascript
   case KB_TYPE.PERSONAL:
     return `kb-docs/personal/${user.user_id}/`;
   ```
   to:
   ```javascript
   case KB_TYPE.PERSONAL:
     if (!args.faitUserId) {
       throw { code: 'USER_RESOLUTION_FAILED', status: 500,
               message: 'Could not resolve FAIT user ID — personal KB listing unavailable' };
     }
     return `kb-docs/personal/${args.faitUserId}/`;
   ```

3. In the `listKbFiles` function, replace:
   ```javascript
   const prefix = getS3Prefix(kb.kb_type, user, { team_id });
   ```
   with:
   ```javascript
   let faitUserId = null;
   if (kb.kb_type === KB_TYPE.PERSONAL) {
     faitUserId = await getFaitUserId(user.user_id);
   }
   const prefix = getS3Prefix(kb.kb_type, user, { team_id, faitUserId });
   ```

The `getS3Prefix` function signature already accepts `(kbType, user, args)` — no change needed there.
The TEAM and CORP cases in `getS3Prefix` remain unchanged.

DO NOT change `KnowledgeBaseService.cs`, `server.js`, or any other files.

---

## Verification
After making changes, verify:
1. `fait/src/FortressAI.Web/Controllers/FirmIntegrationController.cs` — ResolveUser method now does EntraOid DB lookup first, then single-Entra-user fallback
2. `services/fip-mcp/src/utils/fait-user-resolver.js` — new file exists with the getFaitUserId export
3. `services/fip-mcp/src/tools/list_kb_files.js` — has new import, faitUserId logic before getS3Prefix call, PERSONAL case throws if faitUserId is null
