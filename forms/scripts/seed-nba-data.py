#!/usr/bin/env python3
"""
Seed FormIQ with NBA Insurance extraction data.

Requires: pymysql (pip install pymysql)

Usage:
  # Against Aurora (after container is up):
  python3 seed-nba-data.py \
    --host fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com \
    --user fortress_mysql \
    --password $DB_PASSWORD \
    --database formiq_dev

  # Local SQLite dev:
  python3 seed-nba-data.py --sqlite

  # Dry-run (parse only, no DB writes):
  python3 seed-nba-data.py --dry-run [--sqlite or --host ...]
"""

import argparse
import glob
import json
import os
import shutil
import sys
from datetime import datetime, timezone

# ── Path constants ──────────────────────────────────────────────────────────
SCRIPT_DIR   = os.path.dirname(os.path.abspath(__file__))
APP_ROOT     = os.path.dirname(SCRIPT_DIR)           # fortress-form-tools/
NBAIS_DIR    = "/home/fredw/.openclaw/workspace/NBAIS"
PDF_SRC_DIR  = NBAIS_DIR
EXTRACT_DIR  = os.path.join(NBAIS_DIR, "extractions")
QS_JSON_PATH = os.path.join(NBAIS_DIR, "target-question-set.json")
DICT_MD_PATH = os.path.join(NBAIS_DIR, "dictionary-review.md")
UPLOADS_DEST = os.path.join(APP_ROOT, "uploads")

# ACORD form name keywords (to determine FormType)
ACORD_KEYWORDS = ["acord", "workers comp acord", "acord 125", "acord 126",
                  "acord 130", "acord 131", "acord 140",
                  "commercial insurance application",
                  "commercial general liability section",
                  "umbrella / excess section",
                  "acord 140 property section",
                  "workers compensation application"]

NOW = datetime.now(timezone.utc)
NOW_STR = NOW.strftime("%Y-%m-%d %H:%M:%S")


# ── Helpers ──────────────────────────────────────────────────────────────────

def is_acord_form(carrier: str, form_name: str, filename: str) -> bool:
    """Determine if form is an ACORD form vs carrier supplemental."""
    check = " ".join([carrier, form_name, filename]).lower()
    return any(k.lower() in check for k in ACORD_KEYWORDS) or carrier.upper() == "ACORD"


def map_field_type(raw_type: str) -> str:
    """Map extraction JSON types to FormIQ field types."""
    mapping = {
        "text":           "text",
        "date":           "date",
        "checkbox":       "checkbox",
        "checkbox_group": "checkbox",
        "yes_no":         "radio",
        "number":         "number",
        "currency":       "number",
        "textarea":       "text",
        "signature":      "text",
        "radio":          "radio",
        "dropdown":       "dropdown",
        "address":        "text",
        "email":          "text",
        "phone":          "text",
    }
    return mapping.get(raw_type.lower(), "text")


def pick_primary_type(input_types: list) -> str:
    """Pick the most representative type from a list of types."""
    # Priority order: yes_no > checkbox_group > date > number > currency > text
    priority = ["yes_no", "checkbox_group", "checkbox", "date", "number", "currency", "text"]
    for p in priority:
        if p in input_types:
            return map_field_type(p)
    if input_types:
        return map_field_type(input_types[0])
    return "text"


def parse_extraction_json(json_path: str) -> dict:
    """Load and parse an extraction JSON file."""
    with open(json_path, encoding="utf-8") as f:
        return json.load(f)


def pdf_path_for_json(json_path: str) -> str | None:
    """Find the PDF in NBAIS_DIR matching this extraction JSON."""
    base = os.path.splitext(os.path.basename(json_path))[0]
    pdf_path = os.path.join(PDF_SRC_DIR, base + ".pdf")
    return pdf_path if os.path.exists(pdf_path) else None


def collect_forms() -> list[dict]:
    """
    Collect all (json_path, pdf_path, data) tuples for the 15 NBA forms.
    """
    forms = []
    json_files = sorted(glob.glob(os.path.join(EXTRACT_DIR, "*.json")))

    for jf in json_files:
        data = parse_extraction_json(jf)
        pdf  = pdf_path_for_json(jf)
        carrier   = data.get("carrier", "Unknown")
        form_name = data.get("form_name", os.path.basename(jf))
        pages     = data.get("pages", 0)
        questions = data.get("questions", [])
        filename  = os.path.basename(jf).replace(".json", "")

        form_type = "ACORD" if is_acord_form(carrier, form_name, filename) else "Supplemental"

        pdf_dest = None
        if pdf:
            pdf_dest = os.path.join(UPLOADS_DEST, os.path.basename(pdf))

        forms.append({
            "json_path": jf,
            "pdf_src":   pdf,
            "pdf_dest":  pdf_dest,
            "carrier":   carrier,
            "form_name": form_name,
            "form_type": form_type,
            "pages":     pages,
            "questions": questions,
        })

    return forms


def flatten_fields(questions: list) -> list[dict]:
    """
    Flatten questions[].inputs[] into a list of field dicts with section info.
    """
    fields = []
    sort_order = 1

    for q in questions:
        section = q.get("prompt", "")
        for inp in q.get("inputs", []):
            raw_type = inp.get("type", "text")
            fields.append({
                "label":      inp.get("sub_prompt", inp.get("id", "Unknown")),
                "type":       map_field_type(raw_type),
                "section":    section,
                "sort_order": sort_order,
            })
            sort_order += 1

    return fields


def parse_dict_candidates(md_path: str) -> list[dict]:
    """
    Parse 'Notable Unmatched Fields — Candidates for Dictionary Addition'
    from dictionary-review.md. Returns list of dict entries to potentially add.
    """
    candidates = []
    try:
        with open(md_path, encoding="utf-8") as f:
            content = f.read()

        # Find the table under "High-Priority Additions"
        in_table = False
        for line in content.splitlines():
            if "High-Priority Additions" in line:
                in_table = True
                continue
            if in_table:
                # Table rows: | `field_code` | Display Name | Category | Layer | Seen In |
                if line.startswith("|") and "`" in line and "---" not in line and "Suggested" not in line:
                    cols = [c.strip().strip("`") for c in line.split("|") if c.strip()]
                    if len(cols) >= 4:
                        field_code = cols[0]
                        display_name = cols[1]
                        category = cols[2]
                        if field_code and "." in field_code:
                            candidates.append({
                                "field_code":   field_code.replace(".", "_"),
                                "display_name": display_name,
                                "category":     category,
                                "field_type":   "text",
                            })
                elif in_table and line.startswith("#"):
                    break  # End of section
    except Exception as e:
        print(f"  Warning: could not parse dictionary-review.md: {e}")

    return candidates


def parse_question_set() -> tuple[dict, list[dict]]:
    """
    Parse target-question-set.json.
    Returns (question_set_meta, list_of_fields).
    """
    with open(QS_JSON_PATH, encoding="utf-8") as f:
        d = json.load(f)

    qs_meta = {
        "name":        "NBA Builders",
        "vertical":    "Builders",
        "description": "Steve Rettberg's NBA builders program — unified question set across 17 forms",
        "status":      "Active",
    }

    qs_fields = []
    sort_order = 1

    for tier_name, tier_info in d.get("tiers", {}).items():
        tier_label = {
            "tier1_universal":       "Universal",
            "tier2_common":          "Common",
            "tier3_carrier_specific": "Carrier-Specific",
        }.get(tier_name, tier_name)

        for field in tier_info.get("fields", []):
            input_types  = field.get("input_types", ["text"])
            form_count   = field.get("form_count", 1)
            question_txt = field.get("representative_prompt", field.get("field_group", ""))
            category     = field.get("category", "Other")
            section      = f"{tier_label} — {category}"

            qs_fields.append({
                "question_text": question_txt,
                "field_type":    pick_primary_type(input_types),
                "section":       section,
                "is_required":   (tier_name == "tier1_universal"),
                "sort_order":    sort_order,
                "source_form_count": form_count,
            })
            sort_order += 1

    return qs_meta, qs_fields


# ── Database adapters ─────────────────────────────────────────────────────────

class DBAdapter:
    """Abstract DB adapter."""

    def execute(self, sql: str, params=None):
        raise NotImplementedError

    def fetchone(self, sql: str, params=None):
        raise NotImplementedError

    def fetchall(self, sql: str, params=None):
        raise NotImplementedError

    def last_insert_id(self) -> int:
        raise NotImplementedError

    def commit(self):
        raise NotImplementedError

    def close(self):
        raise NotImplementedError


class MySQLAdapter(DBAdapter):
    def __init__(self, host, user, password, database, port=3306):
        import pymysql
        self.conn = pymysql.connect(
            host=host, user=user, password=password,
            database=database, port=int(port),
            charset="utf8mb4",
            autocommit=False,
        )
        self.cursor = self.conn.cursor()

    def execute(self, sql: str, params=None):
        self.cursor.execute(sql, params)

    def fetchone(self, sql: str, params=None):
        self.cursor.execute(sql, params)
        return self.cursor.fetchone()

    def fetchall(self, sql: str, params=None):
        self.cursor.execute(sql, params)
        return self.cursor.fetchall()

    def last_insert_id(self) -> int:
        return self.cursor.lastrowid

    def commit(self):
        self.conn.commit()

    def close(self):
        self.cursor.close()
        self.conn.close()


class SQLiteAdapter(DBAdapter):
    def __init__(self, db_path: str):
        import sqlite3
        self.conn = sqlite3.connect(db_path)
        self.conn.row_factory = sqlite3.Row
        self.cursor = self.conn.cursor()
        self._last_id = None

    def execute(self, sql: str, params=None):
        # Translate MySQL placeholders (%s) → SQLite (?)
        sql = sql.replace("%s", "?")
        self.cursor.execute(sql, params or [])
        self._last_id = self.cursor.lastrowid

    def fetchone(self, sql: str, params=None):
        sql = sql.replace("%s", "?")
        self.cursor.execute(sql, params or [])
        return self.cursor.fetchone()

    def fetchall(self, sql: str, params=None):
        sql = sql.replace("%s", "?")
        self.cursor.execute(sql, params or [])
        return self.cursor.fetchall()

    def last_insert_id(self) -> int:
        return self._last_id or 0

    def commit(self):
        self.conn.commit()

    def close(self):
        self.conn.close()


class DryRunAdapter(DBAdapter):
    """No-op adapter for --dry-run mode."""
    def __init__(self):
        self._id = 1000

    def execute(self, sql: str, params=None):
        pass

    def fetchone(self, sql: str, params=None):
        return None

    def fetchall(self, sql: str, params=None):
        return []

    def last_insert_id(self) -> int:
        self._id += 1
        return self._id

    def commit(self): pass
    def close(self): pass


# ── Seed logic ────────────────────────────────────────────────────────────────

def seed(db: DBAdapter, dry_run: bool = False):
    forms = collect_forms()

    os.makedirs(UPLOADS_DEST, exist_ok=True)

    # ── 1. Copy PDFs ─────────────────────────────────────────────────────────
    pdfs_copied = 0
    for form in forms:
        if form["pdf_src"] and form["pdf_dest"]:
            if not dry_run and not os.path.exists(form["pdf_dest"]):
                shutil.copy2(form["pdf_src"], form["pdf_dest"])
            pdfs_copied += 1
            if dry_run:
                print(f"  [DRY-RUN] Would copy: {os.path.basename(form['pdf_src'])}")
    print(f"  PDFs handled: {pdfs_copied}")

    # ── 2. Seed FormLibrary + FormField records ───────────────────────────────
    forms_seeded  = 0
    fields_seeded = 0

    for form in forms:
        form_name  = form["form_name"]
        carrier    = form["carrier"]
        form_type  = form["form_type"]
        pages      = form["pages"]
        pdf_path   = form["pdf_dest"] or "./uploads/unknown.pdf"
        questions  = form["questions"]

        # Check if already exists
        existing = db.fetchone(
            "SELECT Id FROM FormLibraries WHERE FormName = %s AND CarrierName = %s",
            (form_name, carrier)
        )
        if existing:
            form_id = existing[0]
            print(f"  SKIP (exists) FormLibrary: {form_name}")
        else:
            db.execute(
                """
                INSERT INTO FormLibraries
                  (CarrierName, FormName, FormType, PageCount, Status,
                   PdfBlobPath, VerticalHint, CreatedAt, UpdatedAt)
                VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s)
                """,
                (carrier, form_name, form_type, pages, "Reviewed",
                 pdf_path, "Builders", NOW_STR, NOW_STR)
            )
            form_id = db.last_insert_id()
            forms_seeded += 1
            print(f"  Inserted FormLibrary[{form_id}]: {form_name} ({form_type})")

        # Insert FormFields
        flat_fields = flatten_fields(questions)
        for ff in flat_fields:
            db.execute(
                """
                INSERT INTO FormFields
                  (FormLibraryId, FieldLabel, FieldType, SectionName,
                   AiConfidence, SortOrder, IsRequired, CreatedAt, UpdatedAt)
                VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s)
                """,
                (form_id, ff["label"][:500], ff["type"], ff["section"][:200],
                 0.90, ff["sort_order"], False, NOW_STR, NOW_STR)
            )
            fields_seeded += 1

        if not dry_run:
            db.commit()

    print(f"\n  Forms inserted:  {forms_seeded}")
    print(f"  Fields inserted: {fields_seeded}")

    # ── 3. Seed DictionaryFields from dictionary-review.md ────────────────────
    dict_candidates = parse_dict_candidates(DICT_MD_PATH)
    dict_seeded = 0

    for cand in dict_candidates:
        existing = db.fetchone(
            "SELECT Id FROM DictionaryFields WHERE FieldCode = %s",
            (cand["field_code"],)
        )
        if not existing:
            db.execute(
                """
                INSERT INTO DictionaryFields
                  (FieldCode, DisplayName, Category, FieldType, IsStandard, CreatedAt, UpdatedAt)
                VALUES (%s, %s, %s, %s, %s, %s, %s)
                """,
                (cand["field_code"], cand["display_name"][:300],
                 cand["category"][:100], cand["field_type"], True,
                 NOW_STR, NOW_STR)
            )
            dict_seeded += 1
            if dry_run:
                print(f"  [DRY-RUN] Would add dict entry: {cand['field_code']}")

    if not dry_run:
        db.commit()
    print(f"  Dictionary entries added: {dict_seeded} (skipped {len(dict_candidates) - dict_seeded} existing)")

    # ── 4. Seed QuestionSet ───────────────────────────────────────────────────
    qs_meta, qs_fields = parse_question_set()

    existing_qs = db.fetchone(
        "SELECT Id FROM QuestionSets WHERE Name = %s", (qs_meta["name"],)
    )
    if existing_qs:
        qs_id = existing_qs[0]
        print(f"\n  SKIP (exists) QuestionSet: {qs_meta['name']}")
    else:
        db.execute(
            """
            INSERT INTO QuestionSets
              (Name, Vertical, Description, Status, CreatedBy, CreatedAt, UpdatedAt)
            VALUES (%s, %s, %s, %s, %s, %s, %s)
            """,
            (qs_meta["name"], qs_meta["vertical"], qs_meta["description"],
             qs_meta["status"], "seed-nba-data", NOW_STR, NOW_STR)
        )
        qs_id = db.last_insert_id()
        print(f"\n  Inserted QuestionSet[{qs_id}]: {qs_meta['name']}")

    # Insert QuestionSetFields
    qs_fields_seeded = 0
    for qsf in qs_fields:
        db.execute(
            """
            INSERT INTO QuestionSetFields
              (QuestionSetId, QuestionText, FieldType, SectionName, IsRequired,
               SortOrder, SourceFormCount, CreatedAt, UpdatedAt)
            VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s)
            """,
            (qs_id, qsf["question_text"][:1000], qsf["field_type"],
             qsf["section"][:200], qsf["is_required"],
             qsf["sort_order"], qsf["source_form_count"],
             NOW_STR, NOW_STR)
        )
        qs_fields_seeded += 1

    if not dry_run:
        db.commit()
    print(f"  QuestionSet fields inserted: {qs_fields_seeded}")

    # ── Summary ───────────────────────────────────────────────────────────────
    print("\n" + "═" * 60)
    print(f"  Seeded {forms_seeded} forms, {fields_seeded} fields, {qs_fields_seeded} question set fields")
    print(f"  QuestionSet: '{qs_meta['name']}' (id={qs_id})")
    print(f"  Dictionary entries added: {dict_seeded}")
    print("═" * 60)

    return {
        "forms": forms_seeded,
        "fields": fields_seeded,
        "qs_fields": qs_fields_seeded,
        "dict_added": dict_seeded,
        "qs_name": qs_meta["name"],
    }


# ── QuestionSetFields entity doesn't have CreatedAt/UpdatedAt? check the entity
# Actually looking at the entity — QuestionSetField doesn't have those columns.
# Let me fix the INSERT above.

def seed_v2(db: DBAdapter, dry_run: bool = False):
    """
    Seed with correct column names matching EF Core entity definitions.
    """
    forms = collect_forms()
    os.makedirs(UPLOADS_DEST, exist_ok=True)

    # ── 1. Copy PDFs ──────────────────────────────────────────────────────────
    pdfs_copied = 0
    for form in forms:
        if form["pdf_src"] and form["pdf_dest"]:
            if not dry_run and not os.path.exists(form["pdf_dest"]):
                shutil.copy2(form["pdf_src"], form["pdf_dest"])
            pdfs_copied += 1
    print(f"  PDFs staged to uploads/: {pdfs_copied}")

    # ── 2. FormLibrary + FormField ────────────────────────────────────────────
    forms_seeded  = 0
    fields_seeded = 0

    for form in forms:
        form_name = form["form_name"]
        carrier   = form["carrier"]
        form_type = form["form_type"]
        pages     = form["pages"]
        pdf_path  = form["pdf_dest"] or "./uploads/unknown.pdf"

        # Idempotency check
        row = db.fetchone(
            "SELECT Id FROM FormLibraries WHERE FormName = %s AND CarrierName = %s",
            (form_name, carrier)
        )
        if row:
            form_id = row[0]
            print(f"  SKIP (exists) [{form_id}] {form_name}")
        else:
            db.execute(
                """INSERT INTO FormLibraries
                   (CarrierName, FormName, FormType, PageCount, Status,
                    PdfBlobPath, VerticalHint, CreatedAt, UpdatedAt)
                   VALUES (%s, %s, %s, %s, 'Reviewed', %s, 'Builders', %s, %s)
                """,
                (carrier, form_name, form_type, pages, pdf_path, NOW_STR, NOW_STR)
            )
            form_id = db.last_insert_id()
            forms_seeded += 1
            print(f"  + FormLibrary[{form_id}] {form_type:12} {carrier:25} {form_name}")

        # FormFields (flat)
        flat = flatten_fields(form["questions"])
        for ff in flat:
            db.execute(
                """INSERT INTO FormFields
                   (FormLibraryId, FieldLabel, FieldType, SectionName,
                    AiConfidence, SortOrder, IsRequired, CreatedAt, UpdatedAt)
                   VALUES (%s, %s, %s, %s, 0.90, %s, 0, %s, %s)
                """,
                (form_id, ff["label"][:500], ff["type"],
                 (ff["section"] or "")[:200],
                 ff["sort_order"], NOW_STR, NOW_STR)
            )
            fields_seeded += 1

        if not dry_run:
            db.commit()

    print(f"\n  Forms inserted:  {forms_seeded}")
    print(f"  Fields inserted: {fields_seeded}")

    # ── 3. DictionaryFields from dictionary-review.md ──────────────────────────
    dict_candidates = parse_dict_candidates(DICT_MD_PATH)
    dict_seeded = 0
    for cand in dict_candidates:
        row = db.fetchone(
            "SELECT Id FROM DictionaryFields WHERE FieldCode = %s",
            (cand["field_code"],)
        )
        if not row:
            db.execute(
                """INSERT INTO DictionaryFields
                   (FieldCode, DisplayName, Category, FieldType, IsStandard, CreatedAt, UpdatedAt)
                   VALUES (%s, %s, %s, %s, 1, %s, %s)
                """,
                (cand["field_code"], cand["display_name"][:300],
                 cand["category"][:100], "text", NOW_STR, NOW_STR)
            )
            dict_seeded += 1

    if not dry_run:
        db.commit()
    print(f"  Dictionary entries added: {dict_seeded}")

    # ── 4. QuestionSet ────────────────────────────────────────────────────────
    qs_meta, qs_fields = parse_question_set()

    row = db.fetchone(
        "SELECT Id FROM QuestionSets WHERE Name = %s", (qs_meta["name"],)
    )
    if row:
        qs_id = row[0]
        print(f"\n  SKIP (exists) QuestionSet: {qs_meta['name']}")
    else:
        db.execute(
            """INSERT INTO QuestionSets
               (Name, Vertical, Description, Status, CreatedBy, CreatedAt, UpdatedAt)
               VALUES (%s, %s, %s, 'Active', 'seed-nba-data', %s, %s)
            """,
            (qs_meta["name"], qs_meta["vertical"], qs_meta["description"],
             NOW_STR, NOW_STR)
        )
        qs_id = db.last_insert_id()
        print(f"\n  + QuestionSet[{qs_id}]: {qs_meta['name']}")

    qs_fields_seeded = 0
    for qsf in qs_fields:
        db.execute(
            """INSERT INTO QuestionSetFields
               (QuestionSetId, QuestionText, FieldType, SectionName, IsRequired,
                SortOrder, SourceFormCount)
               VALUES (%s, %s, %s, %s, %s, %s, %s)
            """,
            (qs_id, qsf["question_text"][:1000], qsf["field_type"],
             (qsf["section"] or "")[:200], 1 if qsf["is_required"] else 0,
             qsf["sort_order"], qsf["source_form_count"])
        )
        qs_fields_seeded += 1

    if not dry_run:
        db.commit()
    print(f"  QuestionSet fields inserted: {qs_fields_seeded}")

    # ── Summary ───────────────────────────────────────────────────────────────
    print("\n" + "═" * 60)
    print(f"  ✅ Seeded {forms_seeded} forms, {fields_seeded} fields")
    print(f"  ✅ QuestionSet '{qs_meta['name']}': {qs_fields_seeded} fields (id={qs_id})")
    print(f"  ✅ Dictionary entries added: {dict_seeded}")
    print("═" * 60)

    return {
        "forms": forms_seeded,
        "fields": fields_seeded,
        "qs_fields": qs_fields_seeded,
        "dict_added": dict_seeded,
    }


# ── CLI entrypoint ────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="Seed FormIQ with NBA insurance extraction data.")
    parser.add_argument("--host",     default="fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com")
    parser.add_argument("--port",     default=3306, type=int)
    parser.add_argument("--user",     default="fortress_mysql")
    parser.add_argument("--password", default=None)
    parser.add_argument("--database", default="formiq_dev")
    parser.add_argument("--sqlite",   action="store_true", help="Use local SQLite DB (formtools.db)")
    parser.add_argument("--dry-run",  action="store_true", help="Parse and print, no DB writes")
    args = parser.parse_args()

    print("=" * 60)
    print("  FormIQ NBA Seed Script")
    print(f"  Source: {EXTRACT_DIR}")
    print(f"  Uploads: {UPLOADS_DEST}")
    print("=" * 60)

    # Validate source paths
    if not os.path.isdir(EXTRACT_DIR):
        print(f"ERROR: Extractions directory not found: {EXTRACT_DIR}", file=sys.stderr)
        sys.exit(1)
    if not os.path.exists(QS_JSON_PATH):
        print(f"ERROR: Question set JSON not found: {QS_JSON_PATH}", file=sys.stderr)
        sys.exit(1)

    # Dry-run preflight
    if args.dry_run:
        print("  [DRY-RUN MODE] No database changes will be made.\n")
        forms = collect_forms()
        print(f"  Found {len(forms)} extraction JSONs")
        total_inputs = sum(len(flatten_fields(f["questions"])) for f in forms)
        print(f"  Total field inputs: {total_inputs}")
        _, qs_fields = parse_question_set()
        print(f"  Question set fields: {len(qs_fields)}")
        dict_cands = parse_dict_candidates(DICT_MD_PATH)
        print(f"  Dict candidates from review: {len(dict_cands)}")
        print("\n  Forms that would be seeded:")
        for fm in forms:
            pdf_status = "PDF ✓" if fm["pdf_src"] else "no PDF"
            flat = flatten_fields(fm["questions"])
            print(f"    [{fm['form_type']:12}] {fm['carrier']:25} | {len(flat):4} fields | {pdf_status} | {fm['form_name']}")
        return

    # Connect to DB
    if args.sqlite:
        sqlite_path = os.path.join(APP_ROOT, "FortressFormTools.Web", "formtools.db")
        print(f"  Using SQLite: {sqlite_path}")
        db = SQLiteAdapter(sqlite_path)
    elif args.password:
        print(f"  Connecting to MySQL: {args.user}@{args.host}:{args.port}/{args.database}")
        db = MySQLAdapter(
            host=args.host, user=args.user,
            password=args.password, database=args.database, port=args.port
        )
    else:
        print("ERROR: Provide --password for MySQL or use --sqlite for local dev.", file=sys.stderr)
        parser.print_help()
        sys.exit(1)

    try:
        seed_v2(db, dry_run=False)
    except Exception as e:
        print(f"\nERROR during seed: {e}", file=sys.stderr)
        import traceback; traceback.print_exc()
        sys.exit(1)
    finally:
        db.close()


if __name__ == "__main__":
    main()
