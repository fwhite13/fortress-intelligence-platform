# Hawkeye C2 Fast-Verify — ADO#3092 Avatar NSFW Check on Upload

## Commit: 2688de43

## Working Directory: /home/fredw/projects/fip/fait-v2

## Task
Perform a targeted fast-verify of three specific fixes from Cycle 1 review findings.
Do NOT do a full review — only verify these three fixes are correctly implemented.

---

## Fix 1 — CRITICAL: Bedrock model ID moved to IConfiguration (`AvatarModerationService.cs`)

**Verify ALL of the following:**
1. There is NO `const string ModerationModel` (or similar hardcoded model constant) in `AvatarModerationService.cs`
2. `IConfiguration` is injected in the constructor
3. Model ID is read from `config["Bedrock:AvatarModerationModelId"]` (or equivalent config key)
4. Key `"AvatarModerationModelId"` is present in `appsettings.json` under the `"Bedrock"` section

**Read these files:**
- Find `AvatarModerationService.cs`: search under src/ or Services/ directories
- `appsettings.json` at repo root or relevant project root

---

## Fix 2 — IMPORTANT: S3 avatar URL base is config-driven (`Program.cs`)

**Verify ALL of the following:**
1. `config["AWS:AvatarBaseUrl"]` is used (with fallback to `https://{bucket}.s3.amazonaws.com` or similar)
2. Key `"AvatarBaseUrl"` is present in `appsettings.json` under the `"AWS"` section

**Read these files:**
- `Program.cs` — find the avatar upload/URL construction section
- `appsettings.json` — check AWS section

---

## Fix 3 — IMPORTANT: Magic-byte MIME validation added (`Program.cs`)

**Verify ALL of the following:**
1. A magic-byte validation block is present in the avatar upload handler in `Program.cs`
2. It validates at minimum: JPEG (`FF D8 FF`), PNG (`89 50 4E 47`), GIF (`47 49 46 38`), WebP (`RIFF....WEBP`) signatures
3. The stream is seeked back to position 0 (or equivalent) after reading magic bytes, so subsequent upload operations are not broken

---

## Output Format

For each fix, report:
- VERIFIED ✅ or FAILED ❌
- Exact file location and line numbers where you found (or didn't find) the evidence
- For failures: what is actually present instead

Then give an overall verdict: **PASS** or **NEEDS-CHANGES**

If NEEDS-CHANGES, list each failing item clearly.

---

## Files to Read

Use bash commands to find and read:
```bash
# Find AvatarModerationService
find /home/fredw/projects/fip/fait-v2 -name "AvatarModerationService.cs" 2>/dev/null

# Find Program.cs files
find /home/fredw/projects/fip/fait-v2 -name "Program.cs" 2>/dev/null

# Find appsettings.json
find /home/fredw/projects/fip/fait-v2 -name "appsettings.json" 2>/dev/null
```

Then read the relevant sections of each file.
