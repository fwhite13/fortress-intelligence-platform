# FIP Project Lessons Learned — Discovery Reference

## Spec Quality Lessons

### Intake underspecification is the #1 source of rework
Business users reliably omit: auth model, error states, edge cases, role carve-outs, and time-based triggers. These are not malicious omissions — business users think in happy paths. Discovery must surface these gaps before spec generation.

### "Admin" is always ambiguous
Every FIP module has had at least one revision cycle caused by "admin" meaning different things to the BA and the developer. Always ask: What can an admin do that a regular user cannot? Who assigns admin status?

### Scope creep happens at the data model
Features that seem small often require more data model changes than anticipated. Always ask: Does this feature need to remember anything? If yes, what table and what retention policy?

### AI feature creep
When a spec includes AI-generated content, always ask: Who reviews it before it's acted on? What's the correction/override mechanism? FIP tools have human review gates (Elise for NEXUS, Fred for FAIT) — specs that omit the human gate tend to generate AI-dependent flows with no fallback.

## Process Lessons

### Discovery-less specs take more Elise cycles
Specs that went through Discovery require on average 1.2 Elise revision cycles. Specs without Discovery average 2.4 cycles. The 15-second Discovery investment saves hours of BA time.

### KB context improves question relevance
Discovery questions generated with KB context (FORGE KB passages) are more domain-specific and require less user explanation than questions generated from narrative alone.

### Phase boundaries prevent scope drift
Each phase (P1, P2) should have explicit out-of-scope sections. "We'll add that in Phase 2" without writing it down leads to Phase 2 being redefined in every conversation.
