# CC Brief: ADO4561 Review Cycle 2 — MemoryFileService.cs Fixes

## File to Modify
`/home/fredw/projects/fip/fait/src/FortressAI.Web/Services/MemoryFileService.cs`

## Context
In `ExportZipAsync`, the S3 fallback branch (inside `if (topics.Count == 0)`) has three confirmed bugs from code review. All three fixes are contained in a single block replacement. `_logger` is already injected via constructor — no new injection needed.

## Current Code to Replace

Find this entire block inside `ExportZipAsync` → `if (topics.Count == 0)`:

```csharp
                // Fall back to S3 listing
                var listResp = await _s3.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = BucketName,
                    Prefix = $"workspaces/{userId}/memory/"
                }, ct);

                foreach (var s3obj in listResp.S3Objects)
                {
                    if (!s3obj.Key.EndsWith(".md")) continue;
                    var filename = s3obj.Key.Split('/').Last();
                    try
                    {
                        var response = await _s3.GetObjectAsync(BucketName, s3obj.Key, ct);
                        using var reader = new StreamReader(response.ResponseStream);
                        var content = await reader.ReadToEndAsync(ct);
                        var entry = zip.CreateEntry(filename);
                        await using var entryStream = entry.Open();
                        await using var writer = new StreamWriter(entryStream);
                        await writer.WriteAsync(content);
                    }
                    catch { /* skip unreadable files */ }
                }
```

## Replace With

```csharp
                // Fall back to S3 listing (paginated — matches codebase pattern)
                var listReq = new ListObjectsV2Request
                {
                    BucketName = BucketName,
                    Prefix = $"workspaces/{userId}/memory/"
                };
                ListObjectsV2Response listResp;
                do
                {
                    listResp = await _s3.ListObjectsV2Async(listReq, ct);
                    foreach (var s3obj in listResp.S3Objects)
                    {
                        if (!s3obj.Key.EndsWith(".md")) continue;
                        if (s3obj.Key.Split('/').Last().Equals("MEMORY.md", StringComparison.OrdinalIgnoreCase)) continue; // handled by dedicated block below
                        var filename = s3obj.Key.Split('/').Last();
                        try
                        {
                            var response = await _s3.GetObjectAsync(BucketName, s3obj.Key, ct);
                            using var reader = new StreamReader(response.ResponseStream);
                            var content = await reader.ReadToEndAsync(ct);
                            var entry = zip.CreateEntry(filename);
                            await using var entryStream = entry.Open();
                            await using var writer = new StreamWriter(entryStream);
                            await writer.WriteAsync(content);
                        }
                        catch (OperationCanceledException)
                        {
                            throw; // propagate cancellation — do not swallow
                        }
                        catch (AmazonS3Exception ex) when (ex.ErrorCode is "NoSuchKey" or "AccessDenied")
                        {
                            _logger.LogWarning("[MemoryFile] Skipping {Key}: {ErrorCode}", s3obj.Key, ex.ErrorCode);
                        }
                        // All other exceptions propagate — systemic failures (throttling, expired creds) surface as 500
                    }
                    listReq.ContinuationToken = listResp.NextContinuationToken;
                } while (listResp.IsTruncated);
```

## What Changed and Why
- **Fix C1 (Critical):** Replaced bare `catch { }` with typed catches: re-throw `OperationCanceledException`, log-and-skip `AmazonS3Exception` with NoSuchKey/AccessDenied only. All other exceptions now propagate (systemic S3 failures surface as 500, not silent empty zip).
- **Fix C2 (Critical):** Added `MEMORY.md` skip guard immediately after the `.md` extension check. `ListObjectsV2` returns MEMORY.md under the `memory/` prefix; the dedicated block below the if/else already handles it — adding it in the loop creates a duplicate zip entry.
- **Fix I1 (Important):** Wrapped the single `ListObjectsV2Async` call in the standard `do { ... } while (listResp.IsTruncated)` pagination loop with `ContinuationToken`, matching the pattern used everywhere else in the codebase (`KbDocumentService.cs` lines ~384–403).

## Constraints
- Only modify the fallback block described above. Do NOT touch any other code.
- Do NOT change the `else` branch (DB-backed topics path).
- Do NOT change the unconditional MEMORY.md block below the if/else.
- Do NOT add any new using statements (all types already imported).
- After making the edit, verify the file compiles conceptually (no syntax errors).

## Acceptance Criteria
1. The bare `catch { }` is gone — replaced with typed catch blocks
2. A `MEMORY.md` skip guard exists immediately after the `.md` check
3. The `ListObjectsV2Async` call is inside a `do { ... } while (listResp.IsTruncated)` loop
4. No other code in the file is modified
