# Security Scan Brief: ADO4620 — PPTX Converter Service

You are performing a security code review. Analyze the following files and findings. Report each finding with: Severity, Finding Title, File/Line, Description, and Verdict (BLOCK / WARN / NOTE / PASS).

---

## Files Under Review

### File 1: fait/pptx-converter/server.js (Node/Express converter service)

```javascript
const express = require('express');
const { S3Client, GetObjectCommand } = require('@aws-sdk/client-s3');
const { Upload } = require('@aws-sdk/lib-storage');
const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');

const app = express();
app.use(express.json());

const PORT = process.env.PORT || 3001;
const CONVERTER_API_KEY = process.env.CONVERTER_API_KEY || '';
const AWS_REGION = process.env.AWS_REGION || 'us-east-1';

const s3 = new S3Client({ region: AWS_REGION });

function authMiddleware(req, res, next) {
    if (!CONVERTER_API_KEY) return next(); // dev mode — skip auth
    const authHeader = req.headers['authorization'] || '';
    if (authHeader !== `Bearer ${CONVERTER_API_KEY}`) {
        return res.status(401).json({ error: 'Unauthorized' });
    }
    next();
}

app.get('/health', (req, res) => {
    res.json({ status: 'ok' });
});

app.post('/convert', authMiddleware, async (req, res) => {
    const { artifactId, s3Key, userId, outputBucket } = req.body || {};
    if (!artifactId || !s3Key || !userId || !outputBucket) {
        return res.status(400).json({ error: 'Missing required fields: artifactId, s3Key, userId, outputBucket' });
    }

    const pptxPath = `/tmp/${artifactId}.pptx`;
    const pdfPath = `/tmp/${artifactId}.pdf`;
    // ...
    const lo = spawn('libreoffice', [
        '--headless',
        `--env:UserInstallation=file:///tmp/lo-profile-${artifactId}`,
        '--convert-to', 'pdf',
        '--outdir', '/tmp',
        pptxPath
    ]);
    // ...
    const previewS3Key = `workspaces/${userId}/previews/temp/${artifactId}.pdf`;
    // ...
    // finally block:
    try { fs.unlinkSync(pptxPath); } catch (_) {}
    try { fs.unlinkSync(pdfPath); } catch (_) {}
    try { fs.rmSync(`/tmp/lo-profile-${artifactId}`, { recursive: true, force: true }); } catch (_) {}
});
```

**Key Questions:**
1. Is `artifactId` validated as a UUID before use in `/tmp/${artifactId}.pptx` and S3 key construction? If not, path traversal is possible (e.g., `../` escaping /tmp).
2. Is `userId` validated as a UUID before use in the S3 key `workspaces/${userId}/previews/temp/${artifactId}.pdf`? If not, S3 key corruption possible.
3. `spawn()` is used (not `exec`), which avoids shell injection. Is `shell: false` (the default)? No shell string concatenation of user input? Confirm safe.
4. Auth middleware: comparison uses `===` (not `crypto.timingSafeEqual`). Is this a timing attack risk? At what severity for an internal-only service?
5. `CONVERTER_API_KEY` defaults to empty string — auth is skipped entirely in that case. Is this acceptable? 
6. Temp file cleanup: `finally` block runs cleanup. If `spawn` throws before the finally, or if process crashes, do temp files persist?

### File 2: fait/pptx-converter/Dockerfile

```dockerfile
FROM node:20-slim
RUN apt-get update && apt-get install -y --no-install-recommends \
    libreoffice-writer libreoffice-impress libreoffice-calc \
    libgl1 libglib2.0-0 libsm6 libxrender1 libxext6 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY package.json ./
RUN npm install --production
COPY server.js ./
EXPOSE 3001
CMD ["node", "server.js"]
```

**Key Questions:**
1. Does the container run as root? There is no `USER` directive — node:20-slim runs as root by default.
2. Only `package.json` and `server.js` are copied explicitly — no `COPY . .` that would include secrets. Is this safe?
3. Is `EXPOSE 3001` a public-access concern? Service is ECS Fargate internal-only.
4. `npm install --production` — good practice, no dev deps. Any concern with pinned versions?

### File 3: ArtifactPreviewController.cs (ConvertPptx + PreviewStatus actions)

```csharp
[HttpPost("{id:guid}/convert-pptx")]
[Authorize]
public async Task<IActionResult> ConvertPptx(Guid id)
{
    var artifact = await db.WorkspaceUploads.FirstOrDefaultAsync(u => u.Id == id);
    if (artifact == null) return NotFound();
    var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (artifact.UserId.ToString() != userId) return Forbid();
    // ... calls converter ...
}

[HttpGet("{id:guid}/preview-status")]
[Authorize]  
public async Task<IActionResult> PreviewStatus(Guid id)
{
    var artifact = await db.WorkspaceUploads.FirstOrDefaultAsync(u => u.Id == id);
    if (artifact == null) return NotFound();
    var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (artifact.UserId.ToString() != userId) return Forbid();
    // ... returns previewUrl only if owner matches ...
}
```

**Key Questions:**
1. Both `ConvertPptx` and `PreviewStatus` check ownership (`artifact.UserId.ToString() != userId`) before returning data. Is this correctly enforced?
2. Does `PreviewStatus` disclose `PreviewS3Key` directly, or only a signed token URL?
3. Are the endpoints `[Authorize]` protected?

### File 4: ArtifactPreviewService.cs (GetPreviewStatusAsync + token methods)

```csharp
private static bool CryptographicEquals(string a, string b)
{
    var aBytes = Encoding.UTF8.GetBytes(a);
    var bBytes = Encoding.UTF8.GetBytes(b);
    if (aBytes.Length != bBytes.Length) return false;
    return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
}
```

**Key Questions:**
1. Is `CryptographicOperations.FixedTimeEquals` used for token comparison? (Prevents timing attacks.)
2. Is `PREVIEW_TOKEN_SECRET` required — does it throw if not configured?
3. `GetPreviewStatusAsync` fetches artifact with `u.Id == artifactId && u.UserId == userId` — is ownership enforced at DB level?

### File 5: PptxPreviewPanel.razor (polling logic)

```csharp
while (true) {
    if ((DateTime.UtcNow - started).TotalSeconds > 120) { /* timeout */ }
    await Task.Delay(2000, ct);
    var (isReady, previewUrl) = await PreviewSvc.GetPreviewStatusAsync(ArtifactId, userId);
    // ...
}
```

**Key Questions:**
1. Does the polling loop have a timeout (120s)? Yes — confirm this prevents infinite loops.
2. Does the loop use a CancellationToken to stop on component disposal?
3. Is `userId` obtained from `AuthenticationStateProvider` (server-side trust) rather than from user-controlled input?

---

## Priority Security Matrix

| # | Check | Expected Safe Pattern | Risk if Wrong |
|---|-------|----------------------|---------------|
| 1 | `artifactId` UUID validation | UUID regex check before use in path/S3 | Path traversal → BLOCK |
| 2 | `userId` UUID validation | UUID regex check before use in S3 key | S3 key corruption → BLOCK |
| 3 | LibreOffice spawn shell injection | `spawn()` array args, no shell:true | Command injection → BLOCK |
| 4 | API key timing attack | `timingSafeEqual` or equivalent | Token oracle → WARN |
| 5 | Container runs as root | Non-root USER directive | Privilege escalation → WARN |
| 6 | Auth bypass in dev mode | Acceptable if documented | Low risk → NOTE |
| 7 | Temp file persistence on crash | finally block cleanup | Disk exhaustion → NOTE |
| 8 | PreviewStatus ownership | `UserId != userId` → Forbid | IDOR → BLOCK |
| 9 | HMAC token comparison | FixedTimeEquals | Timing attack → WARN |

---

## Instructions

For each finding above, determine:
- **BLOCK** (Critical/High-confidence): Path traversal without UUID validation, shell injection, IDOR
- **WARN** (Medium-confidence, non-blocking): Timing attacks, running as root, medium findings
- **NOTE** (Informational): Dev mode auth bypass, temp file edge cases

Provide a final overall verdict: **PASS**, **WARN**, or **BLOCK**.

Output a structured security report with:
1. Executive summary (1-2 sentences)
2. Findings table (Severity | ID | Title | File | Description | Recommendation)
3. Final verdict with justification
