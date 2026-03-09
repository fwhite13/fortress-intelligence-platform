#!/usr/bin/env python3
"""
Seed script for FormIQ Data Dictionary — Len's naming conventions.

UPSERT-based: updates existing records by FieldCode, inserts new ones.
Connects to Aurora MySQL using env vars or defaults.

Usage:
    python3 seed-len-conventions.py

Env vars:
    FORTRESS_DB_HOST  (default: localhost)
    FORTRESS_DB_PORT  (default: 3306)
    FORTRESS_DB_USER  (default: formiq)
    FORTRESS_DB_PASS  (default: changeme)
    FORMIQ_DB_NAME    (default: formiq_dev)
"""

import os
import sys
from datetime import datetime, timezone

try:
    import pymysql
except ImportError:
    print("pymysql not installed. Run: pip install pymysql")
    sys.exit(1)

# ── Connection config ──
DB_HOST = os.environ.get("FORTRESS_DB_HOST", "localhost")
DB_PORT = int(os.environ.get("FORTRESS_DB_PORT", "3306"))
DB_USER = os.environ.get("FORTRESS_DB_USER", "formiq")
DB_PASS = os.environ.get("FORTRESS_DB_PASS", "changeme")
DB_NAME = os.environ.get("FORMIQ_DB_NAME", "formiq_dev")

# ── Field definitions following Len's conventions ──
# Format: (FieldCode, DisplayName, Category, FieldType, Description, Synonyms, IsSensitive)
FIELDS = [
    # UPPER_SNAKE — structured data
    ("BIZ_NAME", "Business Name", "General", "text",
     "Legal business name of the insured",
     "Named Insured,Name of Applicant,Insured Name,Legal Business Name", False),

    ("BIZ_ANNUAL_EXP", "Annual Expenses", "Financial", "number",
     "Annual business expenses",
     "Annual Revenue,Business Revenue", False),

    ("DVR_FNAME", "Driver First Name", "General", "text",
     "First name of driver",
     "First Name,Driver First", False),

    ("DVR_LNAME", "Driver Last Name", "General", "text",
     "Last name of driver",
     "Last Name,Driver Last", False),

    ("DVR_LIC", "Driver License Number", "General", "text",
     "Driver's license number — PII",
     "License #,DL Number", True),

    ("DVR_LIC_STATE", "Driver License State", "General", "text",
     "State that issued the driver's license",
     "License State,DL State", False),

    ("VEHICLE_MAKE", "Vehicle Make", "General", "text",
     "Manufacturer of the vehicle",
     "Make,Manufacturer", False),

    ("VEHICLE_MODEL", "Vehicle Model", "General", "text",
     "Model of the vehicle",
     "Model", False),

    ("VEHICLE_YEAR", "Vehicle Year", "General", "number",
     "Model year of the vehicle",
     "Year,Model Year", False),

    ("VEHICLE_VIN", "VIN", "General", "text",
     "Vehicle identification number — PII",
     "Vehicle Identification Number,VIN Number", True),

    ("VEHICLE_VALUE", "Vehicle Value", "Financial", "currency",
     "Stated or actual cash value of the vehicle",
     "Stated Value,ACV", False),

    # PascalCase — legacy
    ("PhysicalAddress", "Physical Address", "Location", "address",
     "Physical street address of the business or risk location",
     "Street Address,Location Address", False),

    # lower_snake — underwriting questions
    ("uw_bankruptcy_5yr", "Bankruptcy in last 5 years", "General", "checkbox",
     "Has the applicant filed for bankruptcy in the last 5 years?",
     "Prior Bankruptcy", False),

    ("uw_cancelled_nonrenewed", "Cancelled/Non-renewed in last 3 years", "General", "checkbox",
     "Has any insurance been cancelled or non-renewed in the last 3 years?",
     "", False),

    ("uw_haul_waste", "Haul Hazardous Waste", "General", "checkbox",
     "Does the applicant haul hazardous waste?",
     "", False),

    # UPPER_SNAKE — healthcare/specialty
    ("CHC_INFECTION_PREV", "Infection Prevention Policies", "General", "textarea",
     "Description of infection prevention policies and procedures",
     "", False),
]


def main():
    now = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S")

    print(f"Connecting to {DB_HOST}:{DB_PORT}/{DB_NAME} as {DB_USER}...")
    conn = pymysql.connect(
        host=DB_HOST,
        port=DB_PORT,
        user=DB_USER,
        password=DB_PASS,
        database=DB_NAME,
        charset="utf8mb4",
    )

    # Check if IsSensitive column exists
    has_is_sensitive = False
    with conn.cursor() as cur:
        cur.execute("""
            SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = %s AND TABLE_NAME = 'DictionaryFields' AND COLUMN_NAME = 'IsSensitive'
        """, (DB_NAME,))
        has_is_sensitive = cur.fetchone() is not None

    if not has_is_sensitive:
        print("Adding IsSensitive column to DictionaryFields...")
        with conn.cursor() as cur:
            cur.execute("ALTER TABLE DictionaryFields ADD COLUMN IsSensitive TINYINT(1) NOT NULL DEFAULT 0")
        conn.commit()
        has_is_sensitive = True

    upserted = 0
    with conn.cursor() as cur:
        for field_code, display_name, category, field_type, description, synonyms, is_sensitive in FIELDS:
            # Check if exists
            cur.execute("SELECT Id FROM DictionaryFields WHERE FieldCode = %s", (field_code,))
            row = cur.fetchone()

            if row:
                # Update
                sql = """
                    UPDATE DictionaryFields
                    SET DisplayName = %s, Category = %s, FieldType = %s,
                        Description = %s, Synonyms = %s, IsSensitive = %s,
                        IsStandard = 1, UpdatedAt = %s
                    WHERE FieldCode = %s
                """
                cur.execute(sql, (display_name, category, field_type, description,
                                  synonyms, int(is_sensitive), now, field_code))
                print(f"  Updated: {field_code}")
            else:
                # Insert
                sql = """
                    INSERT INTO DictionaryFields
                    (FieldCode, DisplayName, Category, FieldType, Description,
                     Synonyms, IsSensitive, IsStandard, CreatedAt, UpdatedAt)
                    VALUES (%s, %s, %s, %s, %s, %s, %s, 1, %s, %s)
                """
                cur.execute(sql, (field_code, display_name, category, field_type,
                                  description, synonyms, int(is_sensitive), now, now))
                print(f"  Inserted: {field_code}")

            upserted += 1

    conn.commit()
    conn.close()

    print(f"\nDone! {upserted} field(s) upserted.")


if __name__ == "__main__":
    main()
