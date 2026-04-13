#!/usr/bin/env python3
"""
WI #1668 — KB Notes Backfill to S3
====================================
Uploads all kb_entries from the DB to S3 in the exact format ForgeService.UploadNoteToS3Async uses,
then triggers Bedrock ingestion for each affected tier.

Usage:
    python3 1668-backfill.py --env dev    # fait_dev DB
    python3 1668-backfill.py --env prod   # fait_prod DB

Idempotent: uses HeadObject to skip already-present objects.
AWS profile: fortress-tools-deployer
"""

import argparse
import json
import logging
import sys
from collections import defaultdict

import boto3
import mysql.connector
from botocore.exceptions import ClientError

# ── Config ─────────────────────────────────────────────────────────────────────

DB_HOST = "fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com"
DB_USER = "fortress_mysql"
DB_PASS = "=RiQOSU5To4aE3F^"
DB_PORT = 3306
S3_BUCKET = "fortress-tools"
AWS_PROFILE = "fortress-tools-deployer"     # S3 operations
AWS_BEDROCK_PROFILE = "openclaw-bedrock"    # Bedrock ingestion (fortress-tools-deployer lacks bedrock:StartIngestionJob)
AWS_REGION = "us-east-1"

# KbTier enum values (must match C# enum)
TIER_PERSONAL  = 0
TIER_TEAM      = 1
TIER_CORPORATE = 2
TIER_DEVELOPER = 3

TIER_NAMES = {
    TIER_PERSONAL:  "Personal",
    TIER_TEAM:      "Team",
    TIER_CORPORATE: "Corporate",
    TIER_DEVELOPER: "Developer",
}

# KB + DataSource IDs — same KBs used for both dev and prod (shared KBs)
KB_CONFIG = {
    "dev": {
        TIER_PERSONAL:  {"kb_id": "ZCEZCJGHQC", "ds_id": "3X5E9L4HAC"},
        TIER_TEAM:      {"kb_id": "NRGEACKSBJ", "ds_id": "VYMEB3BA12"},
        TIER_CORPORATE: {"kb_id": "WYSKBKWHPL", "ds_id": "O6DPFQ08WN"},
        TIER_DEVELOPER: {"kb_id": "EE1X6QJ9WH", "ds_id": "CWZRCFWDEV"},
    },
    "prod": {
        TIER_PERSONAL:  {"kb_id": "ZCEZCJGHQC", "ds_id": "3X5E9L4HAC"},
        TIER_TEAM:      {"kb_id": "NRGEACKSBJ", "ds_id": "VYMEB3BA12"},
        TIER_CORPORATE: {"kb_id": "WYSKBKWHPL", "ds_id": "O6DPFQ08WN"},
        # No Developer KB in prod
    },
}

# ── Logging ────────────────────────────────────────────────────────────────────

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(message)s",
    handlers=[logging.StreamHandler(sys.stdout)],
)
log = logging.getLogger(__name__)

# ── S3 Key helpers (mirror ForgeService.cs exactly) ───────────────────────────

def get_note_s3_key(tier: int, user_id: str, team_id, note_id: int) -> str:
    if tier == TIER_TEAM:
        return f"kb-docs/teams/{team_id}/note-{note_id}.txt"
    elif tier == TIER_CORPORATE:
        return f"kb-docs/fortress/note-{note_id}.txt"
    elif tier == TIER_DEVELOPER:
        return f"kb-docs/dev/note-{note_id}.txt"
    else:  # Personal (default)
        return f"kb-docs/personal/{user_id}/note-{note_id}.txt"


def get_note_metadata_key(s3_key: str) -> str:
    return f"{s3_key}.metadata.json"


def build_note_text(title: str, content: str, tags) -> str:
    """Replicate: $"# {entry.Title}\n\n{entry.Content}" + optional tags line."""
    text = f"# {title}\n\n{content}"
    if tags and str(tags).strip():
        text += f"\n\nTags: {tags}"
    return text


def build_metadata_json(tier: int, user_id: str, team_id) -> str:
    """Replicate ForgeService metadata sidecar logic."""
    if tier == TIER_TEAM:
        attrs = {"teamId": str(team_id)}
    else:
        attrs = {"ownerId": str(user_id)}
    metadata = {"metadataAttributes": attrs}
    return json.dumps(metadata, indent=2)


# ── S3 helpers ─────────────────────────────────────────────────────────────────

def object_exists(s3_client, bucket: str, key: str) -> bool:
    try:
        s3_client.head_object(Bucket=bucket, Key=key)
        return True
    except ClientError as e:
        if e.response["Error"]["Code"] in ("404", "NoSuchKey"):
            return False
        raise


# ── Main ───────────────────────────────────────────────────────────────────────

def run_backfill(env: str):
    db_name = f"fait_{env}"
    log.info(f"=== WI #1668 BACKFILL START | env={env} | db={db_name} | bucket={S3_BUCKET} ===")

    # AWS clients
    session = boto3.Session(profile_name=AWS_PROFILE, region_name=AWS_REGION)
    s3 = session.client("s3")
    # Use separate profile for Bedrock (fortress-tools-deployer lacks bedrock:StartIngestionJob)
    bedrock_session = boto3.Session(profile_name=AWS_BEDROCK_PROFILE, region_name=AWS_REGION)
    bedrock_agent = bedrock_session.client("bedrock-agent")

    # DB connection
    conn = mysql.connector.connect(
        host=DB_HOST,
        user=DB_USER,
        password=DB_PASS,
        port=DB_PORT,
        database=db_name,
        charset="utf8mb4",
    )
    cursor = conn.cursor(dictionary=True)

    cursor.execute("""
        SELECT Id, Title, Content, Tags, Tier, UserId, TeamId
        FROM kb_entries
        ORDER BY Id ASC
    """)
    rows = cursor.fetchall()
    cursor.close()
    conn.close()

    log.info(f"[BACKFILL] Found {len(rows)} kb_entries in {db_name}")

    kb_config = KB_CONFIG[env]
    counts = defaultdict(lambda: {"uploaded": 0, "skipped": 0, "errors": 0})

    for row in rows:
        note_id   = row["Id"]
        title     = row["Title"] or ""
        content   = row["Content"] or ""
        tags      = row["Tags"]
        tier      = int(row["Tier"])
        user_id   = str(row["UserId"])
        team_id   = row["TeamId"]
        tier_name = TIER_NAMES.get(tier, f"unknown({tier})")

        # Skip Developer tier in prod (no KB configured)
        if env == "prod" and tier == TIER_DEVELOPER:
            log.info(f"[BACKFILL] tier={tier_name} id={note_id} status=skipped_no_prod_kb")
            counts[tier_name]["skipped"] += 1
            continue

        s3_key = get_note_s3_key(tier, user_id, team_id, note_id)
        meta_key = get_note_metadata_key(s3_key)

        try:
            # Idempotency check — skip if .txt already present
            if object_exists(s3, S3_BUCKET, s3_key):
                log.info(f"[BACKFILL] tier={tier_name} id={note_id} key={s3_key} status=skipped")
                counts[tier_name]["skipped"] += 1
                continue

            # Upload .txt
            note_text = build_note_text(title, content, tags)
            s3.put_object(
                Bucket=S3_BUCKET,
                Key=s3_key,
                Body=note_text.encode("utf-8"),
                ContentType="text/plain",
            )

            # Upload .metadata.json
            meta_json = build_metadata_json(tier, user_id, team_id)
            s3.put_object(
                Bucket=S3_BUCKET,
                Key=meta_key,
                Body=meta_json.encode("utf-8"),
                ContentType="application/json",
            )

            log.info(f"[BACKFILL] tier={tier_name} id={note_id} key={s3_key} status=uploaded")
            counts[tier_name]["uploaded"] += 1

        except Exception as e:
            log.error(f"[BACKFILL ERROR] tier={tier_name} id={note_id} key={s3_key} error={e}")
            counts[tier_name]["errors"] += 1

    # ── Summary ───────────────────────────────────────────────────────────────
    log.info("")
    log.info("[BACKFILL COMPLETE]")
    total_uploaded = 0
    total_skipped = 0
    tiers_needing_ingestion = []
    for tier_name in ["Personal", "Team", "Corporate", "Developer"]:
        c = counts[tier_name]
        log.info(f"  {tier_name}: {c['uploaded']} uploaded, {c['skipped']} skipped, {c['errors']} errors")
        total_uploaded += c["uploaded"]
        total_skipped  += c["skipped"]
        if c["uploaded"] > 0:
            tiers_needing_ingestion.append(tier_name)
    log.info(f"  Total: {total_uploaded} uploaded, {total_skipped} skipped")
    log.info("")

    # ── Bedrock ingestion ─────────────────────────────────────────────────────
    if not tiers_needing_ingestion:
        log.info("[INGESTION] No new uploads — skipping ingestion trigger")
        return

    tier_name_to_int = {v: k for k, v in TIER_NAMES.items()}

    for tier_name in tiers_needing_ingestion:
        tier_int = tier_name_to_int[tier_name]
        cfg = kb_config.get(tier_int)
        if not cfg:
            log.warning(f"[INGESTION] No KB config for tier={tier_name} env={env} — skipping")
            continue

        kb_id = cfg["kb_id"]
        ds_id = cfg["ds_id"]
        log.info(f"[INGESTION] Triggering ingestion for tier={tier_name} kb_id={kb_id} ds_id={ds_id}")

        try:
            resp = bedrock_agent.start_ingestion_job(
                knowledgeBaseId=kb_id,
                dataSourceId=ds_id,
            )
            job = resp.get("ingestionJob", {})
            job_id = job.get("ingestionJobId", "?")
            status = job.get("status", "?")
            log.info(f"[INGESTION] tier={tier_name} job_id={job_id} status={status}")
        except Exception as e:
            log.error(f"[INGESTION ERROR] tier={tier_name} error={e}")

    log.info("=== WI #1668 BACKFILL END ===")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="WI #1668 KB Notes Backfill")
    parser.add_argument("--env", required=True, choices=["dev", "prod"], help="Target environment")
    args = parser.parse_args()
    run_backfill(args.env)
