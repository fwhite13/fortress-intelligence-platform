# Review Report: WI#941 — FortressApi Config Key Mismatch Fix

**Reviewer:** Hawkeye (Clint Barton)  
**Commit:** `ca5fd07`  
**Cycle:** 1  
**Verdict:** ✅ PASS (with advisory)

---

## Scope Verification

| Check | Result |
|-------|--------|
| Files changed | `famos/src/FamOs.Web/Program.cs` only |
| Scope clean | ✅ |

---

## Key Hierarchy Verification

| Check | Expected | Actual | Result |
|-------|----------|--------|--------|
| `FortressApi:Endpoint` read first, `FortressApi:BaseUrl` as fallback | Yes | Yes | ✅ |
| `FortressApi:ApiKey` read first, `FortressApi:Key` as fallback | Yes | Yes | ✅ |
| `FortressApi:ApiSecret` read first, `FortressApi:Secret` as fallback | Yes | Yes | ✅ |
| Hardcoded fallback values present at end of each chain | Yes | Yes | ✅ |

---

## Code Reviewed (lines 131–145)

```csharp
var fortressBase = builder.Configuration["FortressApi:Endpoint"]
        ?? builder.Configuration["FortressApi:BaseUrl"]
        ?? "https://api.fortressam.ai";
builder.Services.AddHttpClient("FortressApi", c =>
{
    c.BaseAddress = new Uri(fortressBase);
    c.DefaultRequestHeaders.Add("X-Api-Key",
        builder.Configuration["FortressApi:ApiKey"]
            ?? builder.Configuration["FortressApi:Key"]
            ?? "246191f33f470f136ebb800516f8e10f");
    c.DefaultRequestHeaders.Add("X-Api-Secret",
        builder.Configuration["FortressApi:ApiSecret"]
            ?? builder.Configuration["FortressApi:Secret"]
            ?? "77a883a60a2d941b0c1f038881150141dd3655f449c5dadf97e6ffb7066faf4d");
});
```

---

## Claude Code CLI Invocation

```
cat wi941-review-brief.md | claude --model sonnet -p
```

---

## Findings

### ⚠️ Advisory: Hardcoded Credentials in Source

The `X-Api-Key` and `X-Api-Secret` fallback values are full credential strings committed in plaintext. Even if these are local dev values:

- If the same credentials are ever used in staging/prod with misconfigured env vars, they are permanently in git history
- Secret scanners (truffleHog, GitHub Advanced Security) will flag these on every future scan

**Recommendation:** Follow-up ticket to replace hardcoded fallbacks with `throw new InvalidOperationException(...)` or an `appsettings.Development.json.example`. **Not a blocker for this fix** — the three key corrections are correct and this pattern was pre-existing.

---

## Summary

All three config key hierarchy corrections are properly implemented. The `Endpoint??BaseUrl`, `ApiKey??Key`, and `ApiSecret??Secret` chains follow the correct priority order. Hardcoded safety-net fallbacks are present. Scope is clean — only `Program.cs` touched.

**Verdict: PASS** — cleared for APPROVE/DEPLOY.

---

*Generated: 2026-03-20 | Pipeline Stage: REVIEW*
