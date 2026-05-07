# Hawkeye CC Review Brief — ADO#2872
## FAIT v2: Apply FAIT v1 Visual Design Parity

You are performing an adversarial code review. Be skeptical. Check actual logic, not just surface appearance.

## Files to Review

1. `src/FortressAI.V2.Web/Theme/FipTheme.cs` — MudBlazor theme
2. `src/FortressAI.V2.Web/wwwroot/css/fortress.css` — CSS design system (2120 lines)
3. `src/FortressAI.V2.Web/Components/App.razor` — HTML shell

## Critical Checklist (verify each explicitly)

### FipTheme.cs
- [ ] `Primary = "#1a2332"` (must NOT be `#0066CC`)
- [ ] NO `PaletteDark` block anywhere in the file
- [ ] Namespace = `FortressAI.V2.Web.Theme`
- [ ] `AppbarBackground = "#1a2332"`
- [ ] `DrawerBackground = "#1a2332"`
- [ ] `DrawerIcon = "#d4af37"`
- [ ] `AppbarHeight = "48px"` (in LayoutProperties)
- [ ] `DrawerWidthLeft = "264px"` (in LayoutProperties)
- [ ] Font: Inter, FontSize = "0.9375rem", LineHeight = 1.6

### fortress.css
- [ ] File exists and is non-empty (should be ~2120 lines)
- [ ] Contains `:root` block with CSS variables
- [ ] Key variables present: `--color-primary`, `--color-border`, `--space-1` through `--space-12`, `--text-sm`, `--font-primary`
- [ ] Does NOT contain hardcoded hex colors outside of the `:root` variable definitions

### App.razor
- [ ] `<link rel="stylesheet" href="css/fortress.css" />` present
- [ ] `fortress.css` link comes BEFORE `<link rel="stylesheet" href="css/app.css" />`
- [ ] No hardcoded hex colors in style attributes

### All .razor files
- [ ] Scan all Components/**/*.razor for `style=` or `Style=` attributes containing hex color values (e.g., `#[0-9a-fA-F]{3,6}`)
- [ ] Exception: Onboarding.razor lines ~193/208-213 — these are C# data values for a color picker, NOT UI styling — verify they are indeed in C# data context (not in HTML style attributes)

## Important Checks
- Spot-check 5-10 CSS variable names in fortress.css against FAIT v1 naming: `--color-primary`, `--color-border`, `--space-*` (1-12), `--text-sm`, `--text-xs`, `--font-regular`, `--font-medium`, `--font-semibold`
- Verify `app.css` is still linked and loaded AFTER `fortress.css`
- Check `FipTheme.cs` comment accuracy — does the comment accurately describe what the file does?

## Pass Criteria
- All Critical items pass → PASS
- Any Critical item fails → FAIL
- Important issues only → NEEDS-CHANGES

## Report Format
For each checklist item, state: PASS ✅ or FAIL ❌ with file and line reference.
List any issues found with severity.
Give final verdict: PASS / NEEDS-CHANGES / FAIL.
