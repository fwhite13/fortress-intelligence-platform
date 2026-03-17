# Review Brief: WI828 — FfP Sprint 1 Foundation + Core Chat + Apply to Shape

You are Hawkeye (Clint Barton), code reviewer for the FAIT engineering pipeline.

Review the new `fait-for-powerpoint` repo at `/home/fredw/projects/fip/fait-for-powerpoint/`.

This is a PowerPoint Office Add-in built from scratch in Sprint 1.

## Files to Review

Focus on these key files:
- `public/manifest.xml`
- `manifest.local.xml`
- `vite.config.ts`
- `package.json`
- `src/taskpane/services/pptReader.ts`
- `src/taskpane/services/pptWriter.ts`
- `src/taskpane/hooks/useChat.ts`

## Priority Checks

### HIGH — Manifest Host Names (BLOCKER if wrong)
PowerPoint manifests MUST have `Presentation` (NOT `Workbook`) in 3 places:
1. Top-level `<Hosts><Host Name="Presentation"/></Hosts>`
2. VersionOverrides `<Host xsi:type="Presentation">`
3. Requirements `<Set Name="PowerPointApi" MinVersion="1.5"/>`

Verify BOTH `public/manifest.xml` AND `manifest.local.xml`.

### HIGH — PowerPoint.run() vs Excel.run()
Both `pptReader.ts` and `pptWriter.ts` MUST use `PowerPoint.run()` exclusively. NO `Excel.run`.

### HIGH — declare const PowerPoint: any
Both `pptReader.ts` and `pptWriter.ts` must have `declare const PowerPoint: any;` at the top.

### HIGH — @microsoft/office-js absent from package.json
Office.js is CDN-loaded. `@microsoft/office-js` must NOT appear in dependencies or devDependencies. Only `@types/office-js` allowed.

### MEDIUM — tags.add() scope
Confirm `pptWriter.ts` uses `textRange.text = content` assignment. Confirm no `tags.add()` calls.

### MEDIUM — base: '/ppt-addin/' in vite.config.ts
Vite base must be `/ppt-addin/`. All production manifest URLs must use `/ppt-addin/` prefix.
NOTE: `manifest.local.xml` uses localhost URLs which may NOT include `/ppt-addin/` — this is expected for local dev since Vite dev server doesn't apply base the same way. Flag if production manifest.xml URLs are missing the prefix.

### MEDIUM — Port 3001
`vite.config.ts` must have `server.port: 3001`.

### MEDIUM — GUID b2c3d4e5
`<Id>` in both manifests must contain `b2c3d4e5-f6a7-8901-bcde-f12345678902`.

### MEDIUM — getSlideContext() reads title + body + notes
`pptReader.ts` `getSlideContext()` should read:
- Slide title (via shape name containing 'title' or shape.type === 'title')
- Body text shapes (all shapes with hasText)
- Speaker notes (via `slideData.notes.textFrame.textRange.text`)

### MEDIUM — applyTextToShape() shape lookup
`pptWriter.ts` must find shape by ID using PowerPoint API, write via `textRange.text`.

### LOW — useChat.ts Message interface lean
FfP `useChat.ts` must NOT have FfE-specific fields: `tableData`, `reportSpec`, `formulaSpec`.

### LOW — FfE repo untouched
`git diff HEAD~1 HEAD -- fait-for-excel/` in the fip parent repo should return empty.

## Pre-gathered Evidence

I've already verified the following via grep/cat:

**manifest.xml (public/)**:
- `<Host Name="Presentation"/>` ✓ (top-level Hosts)
- `<Host xsi:type="Presentation">` ✓ (VersionOverrides)
- `<Set Name="PowerPointApi" MinVersion="1.5"/>` ✓ (Requirements)
- GUID: `b2c3d4e5-f6a7-8901-bcde-f12345678902` ✓
- All URLs use `/ppt-addin/` prefix ✓

**manifest.local.xml**:
- `<Host Name="Presentation"/>` ✓ (top-level Hosts)
- `<Host xsi:type="Presentation">` ✓ (VersionOverrides)
- `<Set Name="PowerPointApi" MinVersion="1.5"/>` ✓ (Requirements)
- GUID: `b2c3d4e5-f6a7-8901-bcde-f12345678902` ✓
- localhost URLs WITHOUT `/ppt-addin/` prefix (e.g., `https://localhost:3001/src/taskpane/index.html`)
  - This is a potential issue: `base: '/ppt-addin/'` in vite.config.ts means dev server serves at `/ppt-addin/`. The local manifest localhost URLs should be `https://localhost:3001/ppt-addin/src/taskpane/index.html`.

**vite.config.ts**:
- `base: '/ppt-addin/'` ✓
- `server.port: 3001` ✓

**package.json**:
- `@microsoft/office-js` absent ✓ (only `@types/office-js` present)

**pptReader.ts**:
- `declare const PowerPoint: any;` ✓
- `PowerPoint.run()` at line 24 ✓
- No `Excel.run` ✓
- Reads title, body shapes, speaker notes ✓

**pptWriter.ts**:
- `declare const PowerPoint: any;` ✓
- `PowerPoint.run()` at line 16 ✓
- No `Excel.run` ✓
- Finds shape by ID, writes via `target.textFrame.textRange.text = text` ✓
- No `tags.add()` ✓

**useChat.ts**:
- No `tableData`, `reportSpec`, `formulaSpec` fields ✓

**FfE repo**:
- `git diff HEAD~1 HEAD -- fait-for-excel/` returns empty ✓

## Key Question for Deep Review

Please focus your review on:

1. **manifest.local.xml URL issue**: The localhost URLs in `manifest.local.xml` do NOT include `/ppt-addin/` base. With `base: '/ppt-addin/'` in vite.config, Vite dev server should serve at `/ppt-addin/`. So `https://localhost:3001/src/taskpane/index.html` would 404 — it should be `https://localhost:3001/ppt-addin/src/taskpane/index.html`. Is this a blocker?

2. **pptReader.ts notes access**: The notes are read via `slideData.notes.textFrame.textRange.text` but the `allSlides.load()` call doesn't explicitly load notes. Does the notes read work correctly, or will it return empty/undefined?

3. **Overall code quality**: Are there any other issues in the service files that would affect correctness?

Please provide a verdict: PASS / NEEDS-CHANGES / FAIL with specific issues categorized as Critical / Important / Nitpick.
