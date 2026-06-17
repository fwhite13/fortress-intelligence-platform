# Review Report: WI869
## Verdict: NEEDS-CHANGES
## Review Cycle: 1 of 2

## CC Invocation
```bash
cd /home/fredw/projects/fip/famos
cat ~/projects/fait-for-excel/review-brief-wi869.md | claude --model sonnet -p
```

## Priority Checks

| Check | Result | Evidence |
|-------|--------|----------|
| SetApplicationName("FortressAI") | ✅ | Program.cs line 102 |
| DisableAutomaticKeyGeneration() | ✅ | Program.cs line 103 |
| Both on same AddDataProtection() chain | ✅ | Program.cs lines 100-103 |
| FipModule.FAMOS = 4 in enum | ✅ | FipModule.cs line 9 |
| FipModule.FAMOS in FullName() | ✅ | FipModule.cs line 20 |
| FipModule.FAMOS in ShortName() | ✅ | FipModule.cs line 29 |
| FipModule.FAMOS in Url() | ✅ | FipModule.cs line 38 |
| ActiveModule="FipModule.FAMOS" in MainLayout | ✅ | MainLayout.razor line 15 |
| blazor.server.js (not blazor.web.js) | ✅ | App.razor line 16 |
| No FAIT/FIRM/FORMS files modified | ✅ | git show 4f51202 — only famos/ + FipModule.cs |
| Cookie name ".FortressAI.Session" | ✅ | Program.cs line 33 |
| /health AllowAnonymous | ✅ | Program.cs line 185 |
| FallbackPolicy = DefaultPolicy | ✅ | Program.cs line 42 |
| Dockerfile .NET 9 | ✅ | Dockerfile lines 1, 5 |
| buildspec monorepo root context | ✅ | buildspec.yml line 12 |
| CreateTablesAsync in background Task | ✅ | Program.cs lines 124-126, 148 |
| No EF migrations | ✅ | No Migrations/ directory |

## Issues Found

### 🔴 CRITICAL (Blocking — must fix before merge)

**C1 — `FIP_KEYRING_DB_NAME` defaults to `"fred_dev"`**
- **File:** `Program.cs:84`
- **Code:** `var keyRingDb = builder.Configuration["FIP_KEYRING_DB_NAME"] ?? "fred_dev";`
- **Impact:** If this env var is absent from ECS task definition, app connects DataProtection keyring to a developer's personal DB. With `DisableAutomaticKeyGeneration()` active, keyring will be empty → every auth cookie decrypt fails → all users unable to authenticate.
- **Fix:** Change default from `"fred_dev"` to the canonical shared keyring DB name (e.g. `"fip_keyring"`). Add startup warning log if env var is absent in non-dev environment.

**C2 — `ParkOpportunityAsync` skips `Version++` / `UpdatedAt`**
- **File:** `Domain/LifecycleCommandService.cs:251-273`
- **Impact:** Every other lifecycle command increments `opp.Version++` and sets `opp.UpdatedAt = DateTime.UtcNow`. `ParkOpportunityAsync` modifies the opportunity's flags but never updates the concurrency token. This breaks the optimistic concurrency model — two concurrent park requests both succeed without conflict, and stale-loaded callers will silently overwrite.
- **Fix:** Add `opp.UpdatedAt = DateTime.UtcNow; opp.Version++;` before `SaveChangesAsync()`.

---

### 🟡 IMPORTANT (Should fix — functional bugs)

**I1 — Open redirect via unvalidated `Referer` in `/auth/redirect-to-login`**
- **File:** `Program.cs:191`
- **Code:** `var returnUrl = Uri.EscapeDataString(ctx.Request.Headers.Referer.FirstOrDefault() ?? "/");`
- **Impact:** `Referer` is attacker-controlled. Phishing link can inject `Referer: https://evil.com`, causing FAM OS to redirect to `https://fait.dev.fortressam.ai/?returnUrl=https%3A%2F%2Fevil.com`. Defense relies entirely on FAIT validating `returnUrl` — implicit and fragile.
- **Fix:** Validate referer before passing — reject anything not starting with `/` or not matching a FIP hostname whitelist.

**I2 — `ReopenMarketAsync` emits no outbox event**
- **File:** `Domain/LifecycleCommandService.cs:170-187`
- **Impact:** Every other lifecycle command writes an `OutboxEvent` with `DomainEventType.OpportunityLifecycleChanged`. Market reopen skips this entirely — HubSpot/AMS sync will never see the transition. Functional bug, not cosmetic.
- **Fix:** Add `WriteOutboxAsync(DomainEventType.OpportunityLifecycleChanged, ...)` mirroring other commands.

**I3 — `winningQuoteId` parameter ignored when building `PolicyShadowRecord`**
- **File:** `Domain/LifecycleCommandService.cs:224`
- **Code:** `var winningQuote = opp.Quotes.FirstOrDefault(q => q.IsRecommended);` (ignores `winningQuoteId`)
- **Impact:** Method accepts `winningQuoteId`, uses it in the outbox event payload (line 244), but builds the shadow record from `IsRecommended` flag instead. If caller passes a different ID than the recommended quote, shadow record silently uses wrong data.
- **Fix:** Use `opp.Quotes.FirstOrDefault(q => q.Id == winningQuoteId)` for the shadow record lookup.

**I4 — `CancellationToken` not propagated to DB async calls in background services**
- **File:** `Services/OutboxProcessorService.cs:36-70`, `Services/SignalRecomputeService.cs:37-68`
- **Impact:** During graceful shutdown, host signals cancellation on `ExecuteAsync` but in-flight DB queries/saves run to completion with no cancellation. Large batches or full-table signal recomputes will delay shutdown.
- **Fix:** Pass `CancellationToken ct` through `ProcessBatchAsync()` / `RecomputeAllAsync()` and thread to all EF async calls.

**I5 — `SignalRecomputeService` loads all non-closed opportunities into memory**
- **File:** `Services/SignalRecomputeService.cs:43-47`
- **Impact:** Full graph load (with `Flags` + `Quotes` includes) of all open opportunities on every background cycle. Scalability time-bomb at production load.
- **Fix:** Process in chunks (`Skip`/`Take` batching) or `AsAsyncEnumerable()`. Add index on `UpdatedAt` for incremental recompute.

**I6 — `PayloadJson` logged at `Information` level in OutboxProcessor**
- **File:** `Services/OutboxProcessorService.cs:53-54`
- **Impact:** `PayloadJson` contains serialized business objects (premium amounts, carrier names, actor IDs). INFO-level logging floods CloudWatch with PII/sensitive data.
- **Fix:** Downgrade to `LogDebug`, or log only `evt.Id` + `evt.EventType` at INFO.

**I7 — Dockerfile runs as root (no `USER app`)**
- **File:** `Dockerfile` (no USER directive present)
- **Impact:** `aspnet:9.0` image creates non-root `app` user but Dockerfile never switches to it. Container runs as UID 0. Security risk.
- **Fix:** Add `USER app` after final `WORKDIR /app` line, before `ENTRYPOINT`.

**I8 — No duplicate carrier guard in `RouteToMarketAsync`**
- **File:** `Domain/LifecycleCommandService.cs:62`
- **Impact:** Double-submit or retry will insert duplicate `Submission` rows for the same carrier. No unique constraint at DB or app level.
- **Fix:** Guard with `opp.Submissions.Any(s => s.CarrierName == carrier)` check, or add unique index on `(opportunity_id, carrier_name)`.

---

### ⚪ NITPICK (Minor / Code quality)

| # | File | Issue |
|---|------|-------|
| N1 | `FamOsTask.cs:8` | `Status` is an unconstrained string — use enum |
| N2 | `LifecycleCommandService.cs:345` | `WriteActivityAsync`/`WriteOutboxAsync` are fake async (`await Task.CompletedTask`) |
| N3 | `LifecycleCommandService.cs:367` | Exception types co-located in service file — move to `Domain/Exceptions/` |
| N4 | `Program.cs:107` | `SignalResolver` should be `AddSingleton` not `AddScoped` (no per-request state) |
| N5 | `MainLayout.razor:82` | Silent `catch` swallows all auth state errors — add `ILogger` + log at Warning |
| N6 | `MainLayout.razor:64` | Service locator for auth state instead of cascade — use `[CascadingParameter]` |
| N7 | `SignalResolver.cs:23` | `DominantSignal.Parked` overloaded for closed/lost/parked — consider distinct enum values |
| N8 | `buildspec.yml:13` | SHA-tagged image never pushed to ECR — no immutable rollback targets |
| N9 | `buildspec.yml:24` | AWS Account ID hardcoded in source |
| N10 | `FamOsDbContext.cs:70` | No unique index on `policy_shadow_records.opportunity_id` despite one-to-one relationship |

---

## Verdict

**NEEDS-CHANGES**

The scaffold foundation is structurally sound: auth wiring, DataProtection chain, middleware ordering, Blazor Server plumbing, FipModule registration, and background DB init are all correct. Domain model and lifecycle command pattern are well-conceived.

**Blocking on C1 and C2 — must fix before merge:**
- **C1** (`fred_dev` keyring default) is a production outage waiting to happen. One missing env var from an ECS task definition breaks auth for all users with zero warning.
- **C2** (missing `Version++` in `ParkOpportunityAsync`) is a silent concurrency bug that undermines the optimistic locking model established across every other command.

**Also fix I2 in same PR** — `ReopenMarketAsync` missing outbox event is a functional integration bug, not cosmetic.

I1 may be acceptable if FAIT validates `returnUrl` strictly (confirm with Fred). I3–I8 can be Sprint 2 backlog items if timeline is tight.

Recommend: fix C1, C2, I2 → re-review cycle 2.
