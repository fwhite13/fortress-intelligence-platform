# Sample Spec — TIG (Temporary Insurance Gateway)

## What Makes a Complete Spec

This document illustrates what a well-specified FIP module looks like after Discovery and BA review.

### Section 1: Feature Overview (Example)
TIG is the intake tool for temporary/short-term insurance certificates. Producers upload a client request (PDF or CSV), TIG validates coverage eligibility, and generates a certificate PDF for immediate delivery.

**What makes this good:** Clear scope, named users (producers), named output (certificate PDF), named integration (coverage eligibility API).

### Section 3: Acceptance Criteria (Example)
Given a producer uploads a valid CSV with 10 client rows
When TIG processes the batch
Then 10 certificate records are created, each with status "Pending Eligibility Check"
And the producer receives a batch confirmation email with a job ID

**What makes this good:** Testable, specific, no ambiguity about counts or states.

### Section 9: Out of Scope (Example)
- Client self-service portal (producers manage clients, clients do not log in)
- Batch size > 500 rows (performance testing required before enabling)
- Integration with legacy IVANS system (deferred to v2)

**What makes this good:** Explicit, not assumed. Every deferred item is named.

## Common Discovery Gaps in Past Specs
1. Auth model assumed but not stated — always confirm Entra SSO vs. external registration
2. Batch limits not specified — always ask about max record counts for any bulk operation
3. Error states omitted — always ask "what happens when X fails" for every integration point
4. Role carve-outs missing — "admin" in one module is not the same as "admin" in another
