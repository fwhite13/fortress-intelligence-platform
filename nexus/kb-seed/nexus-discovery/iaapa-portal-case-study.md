# IAAPA Portal — BA Discovery Case Study

## Project Summary
The IAAPA (International Association of Amusement Parks and Attractions) portal is a producer tool for Fortress/Higginbotham. It enables producers to manage IAAPA member insurance submissions, generate proposals, and track renewals. It is an INTERNAL tool — IAAPA members do not log in directly.

## Discovery Questions That Changed the Spec

### Q: Who are the users and how are they licensed?
**Why asked:** The mockup showed a "member profile" page — unclear if this was the insured member's self-service view or the producer's view of the member.
**Answer received:** Internal only — Higginbotham producers and Fortress underwriters. No external member access.
**Impact:** Eliminated a member portal auth model entirely. Auth is Entra SSO only. No invitation flow, no external registration.

### Q: What happens when a proposal is rejected?
**Why asked:** The mockup showed a one-way submission flow with no rejection state.
**Answer received:** Proposals can be revised and resubmitted up to 3 times. After 3 rejections, they go to manual review.
**Impact:** Required a revision counter field, a "revision history" section, and a "manual review queue" state in the data model.

### Q: Is renewal automatic or manually triggered?
**Why asked:** Expiration dates were visible in the mockup but no renewal workflow was shown.
**Answer received:** Renewals are manually triggered by the producer, but the system should surface expiring policies 60 days in advance.
**Impact:** Required a scheduled job + notification model that wasn't in the original scope.

## Lessons for Discovery Questions
- Always ask about the user population — "admin" and "user" mean different things in different FIP contexts
- Always ask about rejection/failure states — mockups only show happy paths
- Always ask about time-based triggers — expiry, renewal, escalation — these always require background jobs
- When a mockup shows data that looks like it belongs to an external party, ask if that party has portal access
