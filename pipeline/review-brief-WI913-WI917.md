# Review Brief — WI#913 & WI#917 (Hawkeye cycle 1)

You are performing a focused code review of two surgical commits. Read all diffs carefully and answer each P1 check explicitly.

---

## WI#913 — FIRM Text Contrast (commit 97c08b6)

### Diff: Meetings.razor
```diff
-            <MudText Typo="Typo.body2" Style="color: var(--color-border);" Class="mt-2">
+            <MudText Typo="Typo.body2" Style="color: var(--color-text-secondary);" Class="mt-2">
                 Click "Join a Meeting" to get started.
             </MudText>
```
Remaining in same file (NOT changed):
- Line 34: `<MudIcon ... Style="color: var(--color-border); font-size: 64px;" />` — this is an icon, NOT text

### Diff: MeetingDetail.razor
```diff
-                                <MudTd Style="color: var(--color-border); font-size: 12px; font-family: monospace;">
+                                <MudTd Style="color: var(--color-text-secondary); font-size: 12px; font-family: monospace;">
                                     @FormatTimestamp(context.StartTimeMs)
                                 </MudTd>
```
Remaining color-border uses in MeetingDetail.razor (NOT changed):
- Lines 84, 117, 148: `border-color: var(--color-border)` on MudButton/div borders — these are BORDER styling, not text color
- Line 90: `border: 1px solid var(--color-border)` on a div — correct border use

### P1 checks for WI#913:
1. Meetings.razor empty state body text (MudText body2): `color: var(--color-border)` removed, replaced with `color: var(--color-text-secondary)` — VERIFY
2. MeetingDetail.razor MudTd timestamp cell: `color: var(--color-border)` removed, replaced with `color: var(--color-text-secondary)` — VERIFY
3. Remaining `color-border` in Meetings.razor is ONLY on a MudIcon (icon color, acceptable) — VERIFY no text elements missed
4. Remaining `color-border` in MeetingDetail.razor are ONLY on border-color properties and div borders, NOT on text — VERIFY
5. Scope check: only 2 files changed in firm/src/FortressIntelligenceRM.Web/ — VERIFY

---

## WI#917 — FAM OS Bare Cancel Buttons (commit eebaadf)

### Diff: AddTaskDialog.razor
```diff
-        <MudButton OnClick="Cancel">Cancel</MudButton>
+        <MudButton Class="famos-btn-outline" OnClick="Cancel">Cancel</MudButton>
         <MudButton Class="famos-btn-primary"
                    OnClick="Submit"
                    Disabled="@(_selectedOpp == null || string.IsNullOrWhiteSpace(_title))">
```

### Diff: CloseOpportunityDialog.razor
```diff
-        <MudButton OnClick="Cancel">Cancel</MudButton>
+        <MudButton Class="famos-btn-outline" OnClick="Cancel">Cancel</MudButton>
         <MudButton Class="famos-btn-danger"
                    OnClick="Submit"
                    Disabled="@(_reason == null)">
```

### Diff: OpportunityCreateDialog.razor
```diff
-        <MudButton OnClick="Cancel">Cancel</MudButton>
+        <MudButton Class="famos-btn-outline" OnClick="Cancel">Cancel</MudButton>
         <MudButton Class="famos-btn-primary"
                    OnClick="Submit" Disabled="@(_saving || string.IsNullOrWhiteSpace(_name))">
             Create
```

### P1 checks for WI#917:
5. AddTaskDialog.razor — Cancel MudButton now has Class="famos-btn-outline": VERIFY
6. CloseOpportunityDialog.razor — Cancel MudButton now has Class="famos-btn-outline": VERIFY
7. OpportunityCreateDialog.razor — Cancel MudButton now has Class="famos-btn-outline": VERIFY
8. Primary/danger submit buttons NOT changed to outline: AddTaskDialog uses famos-btn-primary (unchanged), CloseOpportunityDialog uses famos-btn-danger (unchanged), OpportunityCreateDialog uses famos-btn-primary (unchanged) — VERIFY no primary buttons accidentally got outline
9. Scope: only 3 files changed in famos/src/FamOs.Web/Components/Dialogs/ — VERIFY

---

## Instructions

For each P1 check, state:
- ✅ PASS — exactly what you see confirms the fix is correct
- ❌ FAIL — what is wrong, exact file and line

Then give a VERDICT for each WI: PASS or NEEDS-CHANGES.

Be concise. This is a surgical review of 2-3 line changes per WI.
