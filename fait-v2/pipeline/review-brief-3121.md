# Review Brief — ADO#3121 (+ ADO#3117 Pass-Through Verify)

**Reviewer:** Hawkeye (Clint Barton)  
**Commit to review:** `19f68647`  
**WIs:** ADO#3121 (primary), ADO#3117 (pass-through verification)  
**Review cycle:** 1 of 2

---

## What Changed

Single file: `src/FortressAI.V2.Web/wwwroot/css/fortress.css`

```diff
 .message-bubble.message-user .message-content {
-    background: var(--color-primary-light);
+    background: var(--color-primary);
+    color: var(--color-text-on-primary);
     padding: var(--space-3) var(--space-4);
     border-radius: var(--radius-lg);
     border-top-right-radius: var(--radius-sm);
```

2 lines changed. That's the entire diff.

---

## What to Verify for ADO#3121

1. **Background token** — `var(--color-primary)` is the correct dark navy (`#1a2332`). Should NOT be `var(--color-primary-light)` (`#EEF2F7`). Confirm the replacement is correct.

2. **Text color token** — `var(--color-text-on-primary)` is `#ffffff`. Check that it's defined in `:root` and that it will produce readable white text on the dark navy background.

3. **No hardcoded hex** — Verify neither of the changed lines introduces a hardcoded hex value.

4. **Border audit** — Confirm no `border` property exists on `.message-bubble` or `.message-bubble.message-user` selectors that would create a dark frame effect.

5. **Alignment** — `.message-bubble.message-user .message-content` must have `align-self: flex-end`. No change was made here — verify it's still present and correct.

6. **No regressions** — The rest of the `.message-bubble` ruleset (assistant content, meta) must be unchanged.

---

## ADO#3117 Pass-Through Verification

ADO#3117 passed C3 review at commit `9b352982`. This commit should not have touched the following — verify they remain intact:

| Selector | Property | Expected Value |
|----------|----------|----------------|
| `.chat-empty-state` | `padding` | `var(--space-12) var(--space-8)` |
| `.chat-send-btn` | `width` | `var(--space-10)` |
| `.chat-send-btn` | `height` | `var(--space-10)` |
| `.chat-input-field` | `min-height` | `var(--space-10)` |

---

## MANDATORY: Use Claude Code CLI

Write your review brief, then:
```
cat review-brief-3121.md | claude --model sonnet --print --dangerously-skip-permissions
```

Do NOT reason about code without CC reading it first.

---

## Deliverables

1. Review Report: `pipeline/ADO3121-REVIEW-REPORT.md`
2. Verdict: PASS / NEEDS-CHANGES / FAIL
3. CC invocation must be included in the report
