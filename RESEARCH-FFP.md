# FAIT for PowerPoint (FfP) — Deep Research Report

**Date:** 2026-03-16  
**Author:** Bruce Banner (Research Agent)  
**Status:** Draft v1.0 — For internal use  

---

## Executive Summary

FfP follows the same FAIT Office Add-in taskpane pattern as FAIT for Excel (FfE), but targets PowerPoint. The PowerPoint JS API has matured rapidly — 10 numbered requirement sets (1.1 through 1.10, plus Preview) as of early 2026. Core capabilities are solid: slides, shapes, text, images (base64 in Preview), tables, custom properties, bindings, and selection. Key gaps vs. VBA: no animation API, no chart creation, no slide reordering in-place (workaround exists), limited events. Competitively, the market is crowded with generic prompt-to-deck tools, but **zero competitors combine enterprise KB grounding, data sovereignty, and deep Office Add-in integration**. That's FfP's lane.

---

# Track 1: PowerPoint JavaScript API Surface

## 1. API Requirement Sets Overview

PowerPoint JS API has grown to **10 stable requirement sets** (as of early 2026), plus active Preview APIs. The cadence has accelerated — 1.7 through 1.10 all dropped within 2024–2025.

| Req Set | Released | Key Additions | Min M365 Build |
|---------|----------|---------------|----------------|
| **1.1** | ~2018 | `createPresentation()` only | Build 11001 |
| **1.2** | ~2020 | Insert/delete slides from another PPTX | Build 13426 |
| **1.3** | ~2021 | Add/delete slides, custom tags | Build 14701 |
| **1.4** | 2022 | Shapes, text frames, text ranges, full shape manipulation | Build 15330 |
| **1.5** | 2022 | `getSelectedShapes()`, `getSelectedSlides()`, `getSelectedTextRange()`, `setSelectedSlides()`, Presentation.id | Build 15601 |
| **1.6** | Oct 2024 | Slide background read/write, slide notes, hyperlinks | Build 18129 |
| **1.7** | Dec 2024 | Custom document properties (add/delete/list), `CustomPropertyCollection` | Build 18324 |
| **1.8** | Apr 2025 | **Bindings** (bind shapes by ID), Tables (create/read/write), `BorderProperties`, `FillProperties`, `FontProperties` | Build 18730 |
| **1.9** | Aug 2025 | Advanced table formatting — `Border`, `Borders`, `Margins`, full cell formatting | Build 19127 |
| **1.10** | Jan 2026 | Accessibility alt-text, slide background properties, hyperlinks, bullet format styles, `getItemAt` on collections | Build 19610 |
| **Preview** | Ongoing | `addPicture(base64)` with position/size, `onSlideSelectionChanged` event, `getActiveSlide()`, `AddSlideOptions.index` (insert at position) | M365 Insider |

**⚠️ Critical note on LTSC/perpetual Office:** Requirement sets 1.6–1.10 are marked "Not available" for volume-licensed perpetual Office. Enterprise deployments on Office 2021 LTSC are stuck at ~1.4/1.5. **FfP should target 1.5 as baseline with graceful degradation.**

---

## 2. Slides — Add, Delete, Reorder, Duplicate, Content

### What's Supported

**Add a slide:**
```javascript
await PowerPoint.run(async (context) => {
  // Basic: adds to end using default master/layout
  context.presentation.slides.add();
  
  // With specific master/layout (IDs must be known at coding time)
  context.presentation.slides.add({
    slideMasterId: "2147483690#2908289500",
    layoutId: "2147483691#2499880"
  });
  
  await context.sync();
});
```

- **Add at position:** In Preview only (`AddSlideOptions.index`). Stable API always adds to end.
- **Delete:** `slide.delete()` — by reference or loop through collection
- **Duplicate:** No native duplicate method. Workaround: export presentation, extract slide, re-insert via `presentation.insertSlidesFromBase64()`
- **Reorder:** No direct `moveTo(index)`. Workaround: delete and re-add with matching layout, or use `insertSlidesFromBase64` from a temp file

**Insert slides from another presentation:**
```javascript
// Req Set 1.2+
const targetSlide = context.presentation.slides.getItemAt(1);
context.presentation.insertSlidesFromBase64(base64Pptx, {
  formatting: PowerPoint.InsertSlideFormatting.useDestinationTheme,
  targetSlide: targetSlide
});
```

**Slide count / enumerate slides:**
```javascript
const slides = context.presentation.slides;
slides.load("items");
await context.sync();
console.log(slides.items.length); // slide count
```

**Read slide content:** You can enumerate `slide.shapes` to read all shapes and their text.

### Online vs Desktop Parity
All stable through 1.10 supported in both Web and Desktop (M365). No known parity gaps for stable sets.

### Key Limitation
No direct reorder API. The workaround is clunky for a task pane UX.

---

## 3. Shapes & Text — Read/Write Text, Add Shapes, Style Text

### What's Supported (Req Set 1.4+)

**Enumerate and read shapes:**
```javascript
await PowerPoint.run(async (context) => {
  const slide = context.presentation.slides.getItemAt(0);
  const shapes = slide.shapes;
  shapes.load("items/name,items/shapeType,items/textFrame");
  await context.sync();
  
  shapes.items.forEach(shape => {
    if (shape.textFrame.hasText) {
      shape.textFrame.textRange.load("text");
    }
  });
  await context.sync();
});
```

**Write text to a shape:**
```javascript
shape.textFrame.textRange.text = "Hello from FfP!";
```

**Add a text box:**
```javascript
slide.shapes.addTextBox("FAIT Analysis Results", {
  left: 100, top: 100, width: 400, height: 50
});
```

**Style text (Req Set 1.4+, enhanced in 1.8/1.10):**
```javascript
const range = shape.textFrame.textRange;
range.font.bold = true;
range.font.size = 18;
range.font.color = "#FF0000";
// 1.10 adds: allCaps, strikethrough, subscript, superscript
```

**Add geometric shapes:**
```javascript
slide.shapes.addGeometricShape(PowerPoint.GeometricShapeType.rectangle, {
  left: 50, top: 50, width: 200, height: 100
});
```

**Shape types readable:** `geometric`, `image`, `line`, `group`, `table`, `chart`, `smartArt` (enumerable but not all fully writable)

**Custom tags on shapes (Req Set 1.3+):**
```javascript
shape.tags.add("FAIT_ID", "node-123");
```

### Online vs Desktop
Full parity for 1.4+ on M365.

---

## 4. Images — Insert from URL or Base64, Position/Size

### Current State (Important!)

**Base64 image insert: Preview only (not yet stable)**

The `ShapeCollection.addPicture(base64String, options)` method with `PictureAddOptions` (left, top, width, height) is currently in the **Preview** API set as of early 2026. It was not yet promoted to a numbered requirement set.

**Workaround for stable builds:** Use `Office.context.document.setSelectedDataAsync` with `Office.CoercionType.Image` (Common API approach — works for inserting at cursor but not at specific coordinates):
```javascript
Office.context.document.setSelectedDataAsync(base64ImageData, {
  coercionType: Office.CoercionType.Image,
  imageLeft: 10, imageTop: 10, imageWidth: 200, imageHeight: 150
}, callback);
```

**Preview API (preferred when available):**
```javascript
// Only works with PowerPoint.js preview CDN
slide.shapes.addPicture(base64EncodedString, {
  left: 100,
  top: 50,
  width: 300,
  height: 200
});
```

**Insert image from URL:** Not directly supported by the API. Workaround: fetch the URL from the add-in's JavaScript, convert to base64, then use `addPicture`.

### Online vs Desktop
Preview APIs only available in M365 Insider builds. The Common API workaround works on both.

### Key Limitation
No stable `addPicture` API yet. This is one of the most significant current gaps for FfP. Monitor — likely to be promoted to 1.11 or similar by mid-2026.

---

## 5. Charts — Insert Charts, Bind to Data

### Bad News: Charts Are NOT Creatable from PPT JS API

Unlike Excel's rich chart API, **PowerPoint JS API has no chart creation methods**. Charts that exist in a presentation can be read (the shape's `shapeType` will be `chart`), but you cannot:
- Insert a new chart via JS API
- Modify chart data series
- Change chart type
- Bind a chart to a data source

### What You CAN Do
- Detect chart shapes via `shape.shapeType === PowerPoint.ShapeType.chart`
- Read chart dimensions/position
- Move/resize chart shapes

### Workaround for FfP
To insert data-driven charts in FfP, the recommended pattern is:
1. **Generate chart as image** server-side or in a canvas element (e.g., Chart.js rendered to canvas → `toDataURL()` → base64)
2. **Insert the image** using `addPicture` (Preview) or Common API
3. This gives a static snapshot — not live-linked data

Alternatively, insert a pre-built PPTX snippet containing the chart using `insertSlidesFromBase64`. This is the approach used for complex elements that can't be created via JS API.

### Desktop vs Online
Identical (absent) in both.

---

## 6. Slide Layouts & Masters — Access Templates, Apply to Slides

### What's Supported (Req Set 1.3/1.4+)

```javascript
await PowerPoint.run(async (context) => {
  const slideMasters = context.presentation.slideMasters;
  slideMasters.load("items/id,items/name,items/layouts");
  await context.sync();
  
  const layouts = slideMasters.items[0].layouts;
  layouts.load("items/id,items/name");
  await context.sync();
  
  // Use IDs when adding slides
  layouts.items.forEach(layout => {
    console.log(`${layout.name}: ${layout.id}`);
  });
});
```

**Apply master/layout when adding a slide:** Pass `slideMasterId` and `layoutId` to `SlideCollection.add()`.

**Applying a different layout to an existing slide:** Not directly supported. You'd need to delete the slide and re-add it with the correct layout. This is a significant limitation.

**Enumerate layouts from a slide:**
```javascript
const slide = context.presentation.slides.getItemAt(0);
slide.load("layout,layout/name,slideMaster,slideMaster/name");
await context.sync();
console.log(slide.layout.name);
```

**Slide notes (Req Set 1.6+):**
```javascript
// Read notes
slide.load("notes");
await context.sync();
const notesText = slide.notes.textFrame.textRange.text;

// Write notes
slide.notes.textFrame.textRange.text = "Speaker note from FfP";
```

---

## 7. Selection — Current Slide, Shape, Text Range

### What's Supported (Req Set 1.5+)

**Get selected slides:**
```javascript
const selectedSlides = context.presentation.getSelectedSlides();
selectedSlides.load("items/id,items/name");
await context.sync();
```

**Get selected shapes:**
```javascript
const selectedShapes = context.presentation.getSelectedShapes();
selectedShapes.load("items/id,items/name,items/shapeType");
await context.sync();
```

**Get selected text range:**
```javascript
const textRange = context.presentation.getSelectedTextRange();
// or null-safe version:
const textRange = context.presentation.getSelectedTextRangeOrNullObject();
textRange.load("text");
await context.sync();
if (!textRange.isNullObject) {
  console.log(textRange.text);
}
```

**Set selected slides (programmatic selection):**
```javascript
context.presentation.setSelectedSlides(["slide1Id", "slide2Id"]);
```

**Get active slide (Preview only):**
```javascript
const activeSlide = context.presentation.getActiveSlideOrNullObject();
```

**Old Common API approach (pre-1.5, still works):**
```javascript
Office.context.document.getSelectedDataAsync(
  Office.CoercionType.SlideRange,
  result => console.log(result.value.slides[0].index)
);
```

### Online vs Desktop
Full parity (1.5+).

---

## 8. Animations — Programmatic Control

### Status: **NOT SUPPORTED**

Animations are a complete gap in the PowerPoint JS API. There is:
- No API to add animations to shapes or slides
- No API to read existing animation sequences
- No API to trigger or control slide transitions

This is only possible via COM/VBA (e.g., `Shape.AnimationSettings`, `Slide.TimeLine`).

### Workaround
- Insert slides from a pre-built PPTX template that already has animations baked in
- Animations persist when slide content is modified via JS API (they aren't destroyed)

### FfP Impact
FfP cannot create animated slides from scratch. This is acceptable for enterprise data decks — most corp presentations don't need custom animations, and FAIT's use case is data-driven content generation, not slideshow theatrics.

---

## 9. Presentation Metadata

### What's Supported

**Built-in document properties (Req Set 1.7+):**
```javascript
// Custom properties
const props = context.presentation.customProperties;
props.add("FAIT_Version", "1.0.0");
props.add("LastGeneratedBy", "FfP");

// Built-in props (title, author, etc.) - via Common API
Office.context.document.getFileAsync(Office.FileType.Compressed, {}, result => {
  // Extract from PPTX zip; title is in app.xml
});
```

**Presentation ID:**
```javascript
context.presentation.load("id");
await context.sync();
console.log(context.presentation.id); // Req Set 1.5+
```

**Slide count:**
```javascript
context.presentation.slides.load("items");
await context.sync();
const count = context.presentation.slides.items.length;
```

**Limitation:** Built-in properties like `title` and `author` are not directly exposed as first-class API properties. You need to use the Common API file download approach or custom properties as a proxy. The `CustomPropertyCollection` (1.7+) is excellent for storing FfP-specific metadata on a file.

---

## 10. Events

### Current State (Mostly Absent)

**`onSlideSelectionChanged` — Preview only:**
```javascript
// Preview API only
context.presentation.onSlideSelectionChanged.add(async (args) => {
  console.log("Selection changed");
  // args contains basic change info but not new selection details
});
```

**No stable events for:**
- Shape selection change
- Text change
- Slide added/deleted
- Presentation opened/saved

**Compare to Excel:** Excel has a rich event model (`onChanged`, `onSelectionChanged`, `onActivated` at worksheet/workbook level). PowerPoint is ~3-4 years behind Excel in event surface.

**Available workaround:** Polling with a timer — call `getSelectedSlides()` / `getSelectedShapes()` on an interval. Acceptable for task pane UX but not ideal.

### FfP Design Implication
FfP should design around user-initiated actions ("Apply to selected slide") rather than reactive auto-triggers. This is actually fine — it matches the FAIT for Excel pattern where most operations are user-triggered.

---

## 11. API Requirement Sets — Full Summary Table

| Req Set | Office 2021 | Office 2024 | M365 Web | M365 Desktop |
|---------|-------------|-------------|----------|--------------|
| 1.1–1.4 | ✅ | ✅ | ✅ | ✅ |
| 1.5 | ✅ | ✅ | ✅ | ✅ |
| 1.6–1.10 | ❌ | ❌ | ✅ | ✅ (M365 sub) |
| Preview | ❌ | ❌ | ✅* | ✅ M365 Insider |

*Preview requires beta CDN: `https://appsforoffice.microsoft.com/lib/beta/hosted/office.js`

**Recommended FfP minimum baseline:** PowerPointApi **1.5** (catches ~95% of M365 subscribers, includes selection APIs).  
**Target for full feature set:** PowerPointApi **1.8** (adds bindings + tables).  
**Use feature detection for 1.9/1.10/Preview features.**

---

## 12. Known Gaps vs. COM/VBA

| Capability | VBA/COM | PowerPoint JS API |
|------------|---------|-------------------|
| Create animations | ✅ Full control | ❌ Not supported |
| Create/edit charts natively | ✅ | ❌ (read shape only) |
| Slide reorder by index | ✅ | ⚠️ Workaround (delete+re-add) |
| Apply layout to existing slide | ✅ | ❌ No API |
| Read/write slide transitions | ✅ | ❌ |
| Export slide as image | ✅ | ❌ (no canvas capture) |
| Access SmartArt content | ✅ Full | ⚠️ Read shape dims only |
| Video/audio insert | ✅ | ❌ |
| Trigger macros | ✅ | ❌ |
| File open/close events | ✅ | ❌ (Preview: slide selection only) |
| Insert hyperlinks | ✅ | ✅ Req 1.10 |
| Slide notes | ✅ | ✅ Req 1.6 |
| Custom XML parts | ✅ | ✅ Req 1.3+ |
| Image insert (base64) | ✅ | ⚠️ Preview only |

**Key architectural insight for FfP:** The JS API is powerful enough for the FAIT use case (text generation, shape population, structured slide creation from templates) but cannot do multimedia or animation work. FfP should embrace a "template-first" approach — pre-build PPTX templates in FORGE with the right layouts, animations, and brand assets, then programmatically populate content via JS API.

---

# Track 2: Competitive Landscape — AI PPT Add-ins

## 1. Copilot for PowerPoint (Microsoft)

**Type:** Built-in (not an add-in) — bundled with Microsoft 365 Copilot  
**Nature:** Fully integrated into the PowerPoint ribbon; not a separate add-in

### What It Does
- Generate full presentations from a prompt or Word document
- Redesign slides with AI layout suggestions
- Summarize presentations into key points
- Generate speaker notes automatically
- Rewrite slide content (tone, length, formality)
- Apply brand guidelines from within M365 ecosystem
- "Create Presentation from File" — converts Word/PDF → PPT slides

### Pricing
- Included in **Microsoft 365 Personal/Family** (as of late 2025, ~$3/mo increase)
- Standalone: **$30/user/month** (Microsoft 365 Copilot enterprise plan)
- Copilot Chat in Office apps rolling out broadly mid-2025

### Strengths
- Deepest Office integration possible — accesses your M365 files, org data
- GPT-4 quality (Microsoft/OpenAI partnership)
- Auto-brand with org themes
- Best-in-class results for structured deck generation
- Zero install required

### Weaknesses
- **Sends data to Microsoft/OpenAI cloud — no data sovereignty option**
- Expensive ($30/user/month enterprise)
- Cannot query private KB / FORGE knowledge base
- Output is generic — no domain-specific knowledge
- No API for third-party add-in integration
- Not available for Office 2021 LTSC / perpetual users

### FfP Threat Level: High for generic use cases / Low for enterprise KB use cases

---

## 2. Plus AI for PowerPoint

**Type:** Office Add-in (AppSource — `WA200007130`)  
**Category:** Prompt-to-deck generator

### What It Does
- Generate full decks from prompts, Word docs, PDFs
- "Remix" existing slides: reformat, shorten, extend
- Slide-by-slide AI editing from inside PowerPoint
- Template library with auto-design (fonts, colors, layouts)
- Enterprise branding, custom templates, SOC-2 security
- Works in both PowerPoint and Google Slides
- "Live data snapshots" feature

### Pricing
- Starts at **$10/month** (individual)
- Enterprise plans (custom pricing)

### Strengths
- Actually a native Office Add-in (not a web redirect)
- SOC-2 compliance (enterprise-ready)
- Best template variety vs. peer add-ins
- Google Slides cross-support

### Weaknesses
- Generic AI — no proprietary KB
- Cloud-dependent (data leaves the org)
- G2 rating only 3.2/5 (mediocre UX reviews)
- No FORGE-style structured knowledge grounding

### FfP Threat Level: Medium (same add-in form factor, but no KB)

---

## 3. ChatGPT for PowerPoint (by Twistly)

**Type:** Office Add-in (AppSource — `WA200005566`)  
**Category:** Lightweight AI content assistant  
**Users:** 3M+ professionals, 1,200+ universities

### What It Does
- Generate slides from a topic prompt
- Rewrite, summarize, translate existing slide content
- Speaker notes generation
- ChatGPT-style chat for slide content

### Pricing
- Free trial available
- Paid plans ~**$5/month**

### Strengths
- Very cheap
- 4.6/5 AppSource rating
- Works in older PowerPoint (2013+, Mac, Web)
- Simple UX — conversational interface

### Weaknesses
- Pure content; no design intelligence
- No document/KB grounding
- Windows-only limitation on some features
- No enterprise features (security, branding)
- OpenAI data policy — not sovereign

### FfP Threat Level: Low (no KB, no enterprise, cheap consumer tool)

---

## 4. Beautiful.ai

**Type:** Standalone web app + Office Add-in (limited PPT integration)  
**Category:** Smart design automation

### What It Does
- Auto-layouts that adapt as content is typed/pasted
- "Smart templates" maintain consistent visual hierarchy
- Real-time collaboration, commenting, version control
- Brand kit locking (fonts, colors, logos)
- Team collaboration features
- PowerPoint add-in: generate slides inside PowerPoint using Beautiful.ai's design engine

### Pricing
- **Free:** Basic tier
- **Pro:** ~$12/month (billed annually)
- **Team:** ~$40/user/month

### Strengths
- Best-in-class auto-design output (4.7/5 G2)
- Real design intelligence, not just content generation
- Strong brand control
- Viewer engagement analytics

### Weaknesses
- **Design-first, not knowledge-first** — no KB integration
- Content quality is generic AI
- Rigid template structure limits custom layouts
- SaaS — data goes to Beautiful.ai servers

### FfP Threat Level: Low (design tool, not an enterprise KB tool)

---

## 5. Gamma

**Type:** Standalone web app (not an Office Add-in)  
**Category:** AI-first presentation generator  

### What It Does
- Full deck generation from a single sentence
- Narrative-focused output — "slides that flow like a webpage"
- AI content + image generation
- Web-native interactive presentations (no PPTX export required)
- Can export to PPTX (with fidelity loss)

### Pricing
- **Free tier** available
- **Pro:** ~$18/month

### Strengths
- Fastest deck generation (30 seconds to full deck)
- Creative, bold output style
- Good for brainstorming and ideation

### Weaknesses
- **Not an Office Add-in** — standalone tool, not in PowerPoint
- PPTX export has significant formatting loss
- AI content quality described as "grammatically correct, emotionally flat"
- No enterprise KB integration
- Not suitable for data-driven or structured enterprise presentations

### FfP Threat Level: Minimal (different workflow, not in PowerPoint)

---

## 6. Tome

**Type:** Standalone web app  
**Category:** AI narrative presentation tool

### What It Does
- Generates "Tomes" — web-native interactive narrative documents
- AI-powered content and image generation
- Screen recording integration
- Designed more for storytelling than corporate slides
- Export to PDF (PPTX export limited/lossy)

### Pricing
- Freemium; Pro ~$16/month

### Strengths
- Beautiful, web-native output
- Strong narrative / storytelling focus
- Screen recording for product demos

### Weaknesses
- **Not an Office Add-in at all**
- Not PPTX-compatible workflow
- Niche use case (not enterprise data decks)
- No M365 ecosystem integration

### FfP Threat Level: Minimal (completely different workflow)

---

## 7. Canva for PowerPoint

**Type:** No native Office Add-in — PPTX export workflow only  
**Category:** Design tool

### What It Does
- Design presentations in Canva's web editor
- Export as PPTX (with varying fidelity)
- "Canva for Work" has brand kit features
- AI tools (Magic Write, Magic Design) generate slide content

### Integration with PowerPoint
- **No Office Add-in exists.** Canva operates as a standalone web tool.
- The "integration" is: design in Canva → download as PPTX → open in PowerPoint
- No live connection between Canva and PowerPoint

### Pricing
- Free tier; Pro ~$15/month

### Strengths
- Beautiful designs; massive template library
- Strong brand kit features
- Magic Write AI content generation

### Weaknesses
- Not inside PowerPoint — workflow friction
- PPTX export fidelity varies
- Fonts, animations, effects often don't survive export
- No Office ecosystem integration

### FfP Threat Level: Minimal (design workflow, not data-driven)

---

## 8. Other Notable AI PPT Tools

### Autopilot by Smart Barn
- **Type:** Office Add-in
- **What:** Lightweight ChatGPT-based add-in inside PowerPoint ribbon
- Free to start, minimal features
- Not enterprise-grade

### SlidesAI
- **Type:** Google Slides Add-on (not PowerPoint)
- **What:** AI slide generation from text
- Not relevant for FfP

### MagicSlides
- **Type:** Web tool + limited integrations
- Popular for quick deck generation from PDFs/text

### SlideSpeak
- **Type:** SaaS web tool + API
- **What:** "Done-for-you" AI presentation creation; strong PDF/doc → PPT pipeline
- Has an enterprise API offering
- No direct Office Add-in

### Design.ai / Deckrobot / Presentation.ai
- Various web tools; none are Office Add-ins with KB integration

---

## Competitive Summary Matrix

| Tool | Office Add-in | Enterprise KB | Data Sovereignty | Pricing | Threat Level |
|------|--------------|---------------|------------------|---------|--------------|
| Copilot (Microsoft) | Built-in | M365 only | ❌ Cloud | $30/user/mo | High (generic) |
| Plus AI | ✅ | ❌ | ❌ | $10/mo | Medium |
| ChatGPT for PPT | ✅ | ❌ | ❌ | $5/mo | Low |
| Beautiful.ai | Partial | ❌ | ❌ | $12-40/mo | Low |
| Gamma | ❌ | ❌ | ❌ | $18/mo | Minimal |
| Tome | ❌ | ❌ | ❌ | $16/mo | Minimal |
| Canva | ❌ | ❌ | ❌ | $15/mo | Minimal |
| **FfP (FAIT)** | **✅** | **✅ FORGE** | **✅** | TBD | — |

---

# FfP Opportunity — Where Is the Whitespace?

## The Core Thesis

Every competitor in this space does one of two things:
1. **Generic AI prompting** — "turn a sentence into slides." Fast, cheap, data-blind, cloud-dependent.
2. **Design automation** — auto-layout and visual polish. Beautiful output, zero domain knowledge.

**None of them know anything about your company, your products, your clients, or your data.**

FfP's whitespace is the intersection of:
- **Grounded generation** — content pulled from FORGE KB, not hallucinated from web training data
- **Data sovereignty** — slides built without sending proprietary data to OpenAI, Google, or Microsoft
- **Enterprise workflow integration** — living inside PowerPoint (where the work actually happens), not as a separate web app

## Specific Opportunities

### 1. KB-Grounded Slide Content (Zero Hallucination)
Copilot and Plus AI generate slides from generic AI. They hallucinate statistics, invent product features, and confuse your company's positioning with competitors.

**FfP can:** Pull exact product specs, approved messaging, client data, and research from FORGE to populate slides with factually correct, approved content. Every claim is traceable to a FORGE source.

### 2. Enterprise Data Decks from Structured Data
The #1 enterprise PPT use case is the "status deck" or "data deck" — slide showing KPIs, metrics, pipeline numbers. Every competitor makes a pretty slide about vague topics.

**FfP can:** Follow the FfE pattern — connect to structured data in FORGE (or pulled via the FfE bridge), render charts as images, and populate slides with live enterprise data. No other add-in does this.

### 3. Template Fidelity + Brand Compliance
Beautiful.ai and Plus AI do design, but within their own template systems. FfP would operate inside the customer's existing branded PowerPoint templates (slide masters, layouts), populating the existing brand infrastructure.

**FfP can:** Respect slide masters, use company-defined layouts, maintain approved fonts and colors — all while injecting FORGE content. This is the opposite of Gamma/Tome, which impose their own design systems.

### 4. On-Prem / Sovereign Deployment
Copilot is locked to Microsoft cloud. ChatGPT add-ins use OpenAI APIs. Beautiful.ai/Gamma are SaaS-only.

**FfP can:** Run entirely on-premises or in a customer's sovereign cloud. The FAIT pattern already supports this (FORGE + on-prem LLM option). For regulated industries (finance, pharma, defence, legal), this is non-negotiable.

### 5. Contextual Slide Generation from Active Data
FAIT for Excel excels at contextual understanding — you select cells and FAIT knows what they mean. FfP can do the same: you select a slide or shape, FfP reads the context (existing content, slide notes, surrounding slides), queries FORGE for relevant knowledge, and generates/refines content that fits.

**No competitor does contextual, selection-aware generation from private knowledge.**

### 6. The "Executive Briefing" Use Case
Most competitors optimize for deck-from-scratch generation. The highest-value enterprise PPT use case is the **executive briefing update** — you have an existing 40-slide deck, and you need to update 6 slides with current data and current messaging.

**FfP can:** Identify selected slides, query FORGE for the latest on relevant topics, and refresh slide content in place — preserving layout, structure, and brand while updating the substance.

---

## API Feasibility Assessment for FfP Core Features

| FfP Feature | API Support | Req Set | Notes |
|-------------|-------------|---------|-------|
| Generate slide content (text shapes) | ✅ Solid | 1.4 | Core capability |
| Read selected slide/shape context | ✅ Good | 1.5 | Key for contextual generation |
| Read/write speaker notes | ✅ Good | 1.6 | Excellent for AI notes gen |
| Store FORGE references on shapes | ✅ Good | 1.3 | Custom tags |
| Add slides from FORGE templates | ✅ Good | 1.3 | `insertSlidesFromBase64` |
| Insert KB-sourced images | ⚠️ Preview | Preview | Use Common API fallback |
| Display charts from data | ⚠️ Workaround | any | Chart-as-image approach |
| React to slide navigation | ⚠️ Preview | Preview | `onSlideSelectionChanged` |
| Store metadata on presentation | ✅ Good | 1.7 | Custom properties |
| Table generation | ✅ Good | 1.8/1.9 | Strong table API |

**Verdict: FfP is technically feasible today at 1.5 baseline.** Target 1.8 for tables + bindings. Use Preview APIs for images and events behind a feature flag.

---

## Recommended FfP MVP Feature Set (Based on API Feasibility + Competitive Gap)

1. **FORGE-grounded slide content generation** — select a shape or slide, ask FfP to populate with FORGE knowledge
2. **Data slide creation** — inject structured data from FORGE into existing chart-placeholder slides (as images)
3. **Speaker notes from FORGE** — auto-generate speaker notes citing FORGE sources
4. **FORGE KB search panel** — search FORGE from the task pane, paste approved content into selected shapes
5. **Slide metadata tagging** — tag slides with FORGE node references for traceability
6. **Template-based slide injection** — insert new slides from pre-built FORGE-maintained templates

---

## Sources

- Microsoft Learn: PowerPoint JavaScript API requirement sets (https://learn.microsoft.com/en-us/javascript/api/requirement-sets/powerpoint/)
- Microsoft Learn: Add and delete slides programmatically
- Microsoft Learn: PowerPoint JS API 1.5–1.10 and Preview release notes
- SlideSpeak: Top 5 PowerPoint AI Plugins comparison (Aug 2025)
- SlidePeak: Beautiful.ai vs Gamma (Nov 2025)
- Microsoft AppSource: Plus AI (`WA200007130`), ChatGPT for PowerPoint (`WA200005566`)
- Microsoft 365 Copilot pricing pages (2025)

---

*Research conducted by Bruce Banner (FfP Research Agent) — 2026-03-16*
