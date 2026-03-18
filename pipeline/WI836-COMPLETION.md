# Pipeline Completion: WI836

## Outcome: DEPLOYED ✅
**Date:** 2026-03-17
**Total pipeline time:** ~16 min (16:13 build → 16:29 confirm) — 2 review cycles

---

## What Shipped

Vendorply email triage — mailbox-wide folder search overrides high-confidence DB match.

**Root cause:** `src/engine/classifier.ts` line ~133 did a hard `return` on DB vendor match ≥0.80, bypassing any check of historical email routing.

**Fix (3 files):**
- `src/services/graph-mail.ts` — `searchMailbox(query, top)` using `GET /messages?$search` (mailbox-wide, client_credentials path), `ConsistencyLevel: eventual`
- `src/engine/folder-searcher.ts` — `searchMailbox()` delegation pass-through
- `src/engine/classifier.ts` — replaced hard return with mailbox concentration check: fetches up to 20 messages by vendor name, counts `toRecipients` hits per team member; overrides DB match only when ≥3 messages concentrate on a DIFFERENT member with ≥0.70 confidence. Exception is best-effort (falls back to DB match with audit trail). Added `analyzeMailboxConcentration()` private method.

**CI fix (cycle 2):** `/me/messages` → `/messages` (axios baseURL includes `/users/{mailboxId}` — `/me/` is delegated-flow only); dead `parentFolderId` comment cleaned up.

**Commit:** `97605da`
**Deploy:** `vendorply-triage.service` systemd on SteamServer (first-time unit install)

---

## Pipeline Summary

| Stage | Status | Notes |
|-------|--------|-------|
| PLAN | ✅ | Bug description from Jarvis/Fred |
| BUILD | ✅ | 2 cycles; final commit 97605da; TS clean |
| REVIEW | ✅ | C1 NEEDS-CHANGES (/me/messages); C2 PASS (6/6) |
| SECURITY | ✅ | PASS — read-only Graph call, best-effort fallback |
| APPROVE | ✅ | Standing approval |
| DEPLOY | ✅ | systemd unit created + started; all layers clean |
| VERIFY | ✅ | Natasha PASS (5/5) |
| CONFIRM | ✅ | WI#836 → Done |

---

## Fred Action Items
1. `sudo systemctl enable vendorply-triage.service` — enables auto-start on reboot
2. Flip dry-run mode flag when ready to go live (emails not moving yet)
3. Functional test: send an email from a known vendor that has existing emails routed to a different member — verify override fires in logs
