#!/usr/bin/env bash
# upload-seed.sh — Upload NEXUS Discovery KB seed documents to S3
# Run after FORGE-DevTeam-Shared S3 bucket name is confirmed by Fred/Rob
# Usage: FORGE_KB_BUCKET=<bucket-name> ./upload-seed.sh

set -euo pipefail

# TODO: Replace with actual FORGE-DevTeam-Shared S3 bucket name (confirmed by Fred/Rob)
BUCKET="${FORGE_KB_BUCKET:-TODO_FORGE_KB_S3_BUCKET}"
PREFIX="nexus-discovery"
REGION="us-east-1"
PROFILE="${AWS_PROFILE:-fortress-tools-deployer}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

if [[ "$BUCKET" == TODO_* ]]; then
  echo "ERROR: Set FORGE_KB_BUCKET env var to the actual S3 bucket name" >&2
  echo "  Ask Fred or Rob for the FORGE-DevTeam-Shared KB S3 bucket name" >&2
  exit 1
fi

echo "Uploading seed documents to s3://$BUCKET/$PREFIX/"

for doc in "$SCRIPT_DIR/nexus-discovery/"*.md; do
  filename=$(basename "$doc")
  echo "  Uploading $filename..."
  aws s3 cp "$doc" "s3://$BUCKET/$PREFIX/$filename" \
    --region "$REGION" \
    --profile "$PROFILE" \
    --content-type "text/markdown"
done

echo "Upload complete. Trigger KB sync if not using auto-sync."
echo "Verify with: aws bedrock-agent list-ingestion-jobs --knowledge-base-id <KB_ID> --data-source-id <DS_ID> --region $REGION --profile $PROFILE"
