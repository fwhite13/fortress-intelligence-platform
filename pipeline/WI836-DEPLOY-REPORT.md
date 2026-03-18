# Deploy Report: WI836
**Vendorply triage: mailbox search override**

---

## Outcome: ✅ DEPLOYED

**Deployed by:** War Machine (Rhodey)
**Deploy time:** 2026-03-17 16:26 EDT
**Commit:** `97605da`

---

## Pre-Deploy Snapshot

| Item | Value |
|------|-------|
| Pre-deploy commit | `97605da fix(WI836): use /messages not /me/messages (client_credentials); clean analyzeMailboxConcentration dead code` |
| Service status before deploy | `vendorply-triage.service` — **not yet installed** (first-time systemd deploy) |
| Git status | Already at HEAD (`97605da`) — no pull needed |

> **Note:** `vendorply-triage.service` did not previously exist as a systemd unit. This was a first-time service installation. The unit file was created and installed at `/etc/systemd/system/vendorply-triage.service` as part of this deploy.

---

## Rollback Command (exact)

```bash
cd ~/projects/skunkworks/vendorply-email-triage
git checkout b74570d   # previous commit (pre-cycle-2 fix)
npm run build
sudo systemctl restart vendorply-triage.service
```

If service needs to be removed entirely:
```bash
sudo systemctl stop vendorply-triage.service
sudo systemctl disable vendorply-triage.service
sudo rm /etc/systemd/system/vendorply-triage.service
sudo systemctl daemon-reload
```

---

## Build Output

```
> vendorply-email-triage@1.0.0 build
> tsc
```

**Result: CLEAN — zero errors, zero warnings.**

---

## Service Unit Installed

```ini
[Unit]
Description=Vendorply Email Triage Service
After=network.target

[Service]
Type=simple
User=fredw
WorkingDirectory=/home/fredw/projects/skunkworks/vendorply-email-triage
ExecStart=/usr/bin/node dist/index.js
Restart=on-failure
RestartSec=10
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
```

---

## Service Status After Restart

```
● vendorply-triage.service - Vendorply Email Triage Service
     Loaded: loaded (/etc/systemd/system/vendorply-triage.service; disabled; preset: enabled)
     Active: active (running) since Tue 2026-03-17 16:26:55 EDT; 3s ago
   Main PID: 782224 (node)
      Tasks: 11 (limit: 19005)
     Memory: 40.4M (peak: 41.8M)
        CPU: 261ms
     CGroup: /system.slice/vendorply-triage.service
             └─782224 /usr/bin/node dist/index.js
```

**Status: `active (running)` ✅**

---

## Recent Log Lines (Startup)

```
Mar 17 16:26:55 SteamServer systemd[1]: Started vendorply-triage.service - Vendorply Email Triage Service.
Mar 17 16:26:55 SteamServer node[782224]: [FAIT Vendorply Triage] Initializing...
Mar 17 16:26:55 SteamServer node[782224]: [Init] Connecting to Vendorply database via SSH tunnel...
Mar 17 16:26:55 SteamServer node[782224]: [VendorplyDB] SSH tunnel ready — local port 37093 → vendorply-prod-rds.cr1qki4lk6ir.us-east-1.rds.amazonaws.com:3306
Mar 17 16:26:56 SteamServer node[782224]: [VendorplyDB] MySQL pool connected through SSH tunnel
Mar 17 16:26:56 SteamServer node[782224]: [Init] Database connected — DB vendor lookup (Layer 2) ENABLED
Mar 17 16:26:56 SteamServer node[782224]: [Init] LLM fallback: ENABLED
Mar 17 16:26:56 SteamServer node[782224]: [Init] AttachmentAnalyzer initialized — Layer 3.5 ENABLED
Mar 17 16:26:56 SteamServer node[782224]: [Init] Authenticating with Microsoft Graph...
Mar 17 16:26:56 SteamServer node[782224]: [Init] Graph API authenticated
Mar 17 16:26:56 SteamServer node[782224]: [Init] Refreshing folder cache...
Mar 17 16:26:56 SteamServer node[782224]: [GraphMailService] Folder cache refreshed: 25 folders cached
Mar 17 16:26:56 SteamServer node[782224]: [Init] Ensuring "Needs Triage" folder exists...
Mar 17 16:26:56 SteamServer node[782224]: [Init] "Needs Triage" folder ready (ID: AAMkAGMxMTI3NDE2...PCAAA=)
Mar 17 16:26:56 SteamServer node[782224]: [Init] All services ready. Starting poller...
Mar 17 16:26:56 SteamServer node[782224]: [Poller] Starting — polling every 30s [DRY-RUN MODE — no emails will be moved]
Mar 17 16:26:56 SteamServer node[782224]: [Poller] Polling inbox for unprocessed messages...
Mar 17 16:26:56 SteamServer node[782224]: [Poller] No new unprocessed messages
```

**All layers initialized clean:**
- ✅ DB / SSH tunnel connected (Layer 2 ENABLED)
- ✅ LLM fallback ENABLED
- ✅ AttachmentAnalyzer Layer 3.5 ENABLED
- ✅ Graph API authenticated
- ✅ Folder cache refreshed (25 folders)
- ✅ Poller running (30s interval, DRY-RUN mode)
- ✅ No crash loops, no errors

---

## ADO Updates

| Timestamp | Comment |
|-----------|---------|
| 2026-03-17 20:25 UTC | DEPLOY STARTING — comment ID 724700 |
| 2026-03-17 20:27 UTC | DEPLOY COMPLETE — comment ID 724702 |

---

## Notes

- Service was not previously installed as a systemd unit — this deploy includes first-time unit file creation.
- Service is running in **DRY-RUN mode** (no emails will be moved until DRY_RUN env var is cleared).
- Service is installed but **not enabled** (`systemctl enable` not run — manual start only). Enable if persistent across reboots is desired.
- Natasha to verify live poller behavior and classifier output.

---

**Status: DEPLOY COMPLETE ✅ — Passing to Natasha for VERIFY.**
