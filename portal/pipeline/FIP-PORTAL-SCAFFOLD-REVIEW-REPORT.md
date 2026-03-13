# Review Report: FIP Portal Scaffold

**Task:** FIP-PORTAL-SCAFFOLD
**Agent:** Hawkeye (Clint Barton — code-reviewer)
**Date:** 2026-03-13
**Review Cycle:** 1 of 2
**Commit:** `0d12374`

---

## Verdict: NEEDS-CHANGES

One **Critical** finding (Item 18 — `[Authorize]` vs `[AllowAnonymous]`) must be corrected before this can pass. All other items pass. This is a targeted, verifiable fix — no scope creep required.

---

## Checklist Results

### Project Structure (Items 1–5)

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 1 | `src/FortressPortal.Web/` with `Program.cs`, `appsettings.json`, `Dockerfile`, `.csproj` | ✅ PASS | All four files present |
| 2 | `Components/Pages/Index.razor` — app switcher grid page | ✅ PASS | Present, renders 4-tile grid |
| 3 | `Components/Layout/MainLayout.razor` — outer layout with header/footer | ✅ PASS | Present with `MudAppBar` header |
| 4 | `buildspec.yml` — uses `fortress-tools-portal` ECR repo, `portal-latest` tag | ✅ PASS | ECR repo and tag confirmed |
| 5 | `docker-compose.yml` — port `3334:8080` | ✅ PASS | `"3334:8080"` confirmed |

---

### App Tiles (Items 6–11)

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 6 | All 4 apps present: FAIT, FIRM, FORMS, FORGE | ✅ PASS | All 4 tiles in `Index.razor` |
| 7 | FAIT → `fait.dev.fortressam.ai`, FIRM → `meetings.dev.fortressam.ai`, FORMS → `forms.dev.fortressam.ai` | ✅ PASS | Exact URLs confirmed in `Href` attributes |
| 8 | FORGE tile disabled/greyed with "Coming Soon" — does NOT link to live URL | ✅ PASS | `Disabled="true"`, `app-tile--disabled` CSS class, no `Href`. MudButton has no link. |
| 9 | Each tile has a MudBlazor icon | ✅ PASS | SmartToy (FAIT), VideoCall (FIRM), Assignment (FORMS), Construction (FORGE) |
| 10 | "Open" button on each active tile (FAIT, FIRM, FORMS) | ✅ PASS | `MudButton` with `Href` and text "Open" on all three active tiles |
| 11 | Grid responsive (2-column desktop, 1-column mobile via MudBlazor breakpoints) | ✅ PASS | `xs="12" sm="6" md="6" lg="3"` — 1-col mobile, 2-col sm/md, 4-col lg. Matches requirement. |

---

### Design (Items 12–14)

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 12 | Dark navy background `#0d1117` | ✅ PASS | Set in `portal.css` on `html, body`, `.portal-wrapper`, `MainLayout.razor` inline style |
| 13 | Gold accent `#C9A84C` used for brand element | ✅ PASS | App bar brand text, `.portal-title`, icon fills, "Open" buttons, `app-full-name`, hover border |
| 14 | Header reads "Fortress Intelligence Platform", footer has copyright | ✅ PASS | AppBar brand label = "Fortress Intelligence Platform"; footer = "© 2025 Refuge Group. All rights reserved." |

---

### Auth Stub (Items 15–18)

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 15 | `Program.cs` reads `Cognito__Authority`, `Cognito__ClientId`, `Cognito__ClientSecret` | ✅ PASS | All three read via `builder.Configuration[...]` in the non-stub branch |
| 16 | `UseStubAuth=true` bypasses auth | ✅ PASS | When true: cookie scheme with `FallbackPolicy = null`; no OIDC challenge |
| 17 | `appsettings.json` has `UseStubAuth: true` as default | ✅ PASS | `"UseStubAuth": true` present |
| 18 | `Index.razor` has `[Authorize]` attribute | ❌ **FAIL** | `Index.razor` uses `@attribute [AllowAnonymous]` — **not** `[Authorize]`. The spec requires `[Authorize]` to be present (works with stub auth via null FallbackPolicy). `[AllowAnonymous]` will silently bypass auth enforcement when `UseStubAuth=false` is set for production. **This is the wrong attribute.** |

---

### Dockerfile + Buildspec (Items 19–22)

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 19 | Dockerfile multi-stage: `build` stage + `base/runtime` stage | ✅ PASS | Stages: `base`, `build`, `publish`, `final` |
| 20 | Dockerfile exposes port `8080` | ✅ PASS | `EXPOSE 8080` in `base` stage |
| 21 | `buildspec.yml` tags as `portal-latest`, pushes to `fortress-tools-portal` | ✅ PASS | Both confirmed |
| 22 | No hardcoded AWS account IDs in `buildspec.yml` | ❌ **FAIL** | `AWS_ACCOUNT_ID: 742932328420` is hardcoded in the `env.variables` block. The account ID must come from CodeBuild environment, not be embedded in the spec file. |

---

### Build Integrity (Item 23)

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 23 | Build report confirms 0 Error(s) | ✅ PASS | Build Report states "Build succeeded — 0 Error(s), 0 Warning(s)" |

---

## Issues — Categorized

### ❌ Critical (Blocking)

**Item 18 — `[AllowAnonymous]` instead of `[Authorize]` on `Index.razor`**

- **File:** `src/FortressPortal.Web/Components/Pages/Index.razor`, line 2
- **Current:** `@attribute [AllowAnonymous]`
- **Required:** `@attribute [Authorize]`
- **Why it matters:** The spec explicitly requires `[Authorize]` so the page enforces auth when `UseStubAuth=false`. The stub mode's null `FallbackPolicy` + cookie scheme already allows the page to load without a Cognito challenge in stub mode — `[AllowAnonymous]` is redundant at best, dangerous at worst. With `UseStubAuth=false` and `[AllowAnonymous]`, the page will be publicly accessible with no auth, defeating the purpose of Cognito integration.
- **Fix:** Replace `@attribute [AllowAnonymous]` with `@attribute [Authorize]`

---

### ❌ Important (Blocking)

**Item 22 — Hardcoded AWS Account ID in `buildspec.yml`**

- **File:** `buildspec.yml`, `env.variables` block, last line
- **Current:** `AWS_ACCOUNT_ID: 742932328420`
- **Why it matters:** AWS account IDs must never be hardcoded in source-controlled files. This creates a security exposure and violates the explicit acceptance criteria. The ID should be injected via CodeBuild environment variable configuration (project-level, not in `buildspec.yml`).
- **Fix:** Remove the `AWS_ACCOUNT_ID` line from `env.variables`. The variable will be supplied by the CodeBuild project environment. The rest of `buildspec.yml` already references `$AWS_ACCOUNT_ID` correctly — removing the hardcoded default is all that's needed.

```yaml
# Remove this from env.variables:
#   AWS_ACCOUNT_ID: 742932328420
```

> Note: The `AWS_DEFAULT_REGION: us-east-1` line is acceptable as a default in the spec since it contains no sensitive value.

---

### 📝 Nitpick (Non-blocking)

**Footer year is 2025, not 2026**
- **File:** `src/FortressPortal.Web/Components/Pages/Index.razor`
- **Current:** `© 2025 Refuge Group. All rights reserved.`
- **Suggestion:** Update to `© 2026` to reflect the current year. Low priority — cosmetic.

---

## Summary

| Category | Count |
|----------|-------|
| ✅ PASS | 21 / 23 |
| ❌ Critical | 1 (Item 18 — wrong auth attribute) |
| ❌ Important | 1 (Item 22 — hardcoded account ID) |
| 📝 Nitpick | 1 (footer year) |

The scaffold is otherwise clean. Structure, tiles, URLs, design, Docker, and build integrity all pass. Two targeted fixes required — both are one-line changes. No architectural issues.

---

## Required Fixes for Tony

1. **`Index.razor` line 2:** Change `@attribute [AllowAnonymous]` → `@attribute [Authorize]`
2. **`buildspec.yml`:** Remove `AWS_ACCOUNT_ID: 742932328420` from the `env.variables` block entirely

Optional:
3. **`Index.razor` footer:** Update `© 2025` → `© 2026`

---

*— Hawkeye. Two shots. Both on target. Fix them and this passes clean.*
