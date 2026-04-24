#!/bin/bash
# Upload all proposal-generator templates to S3
BUCKET="fortress-tools"
PREFIX="fip-proposal-templates"
TEMPLATES_DIR="$(dirname "$0")"

aws s3 cp "$TEMPLATES_DIR/verticals/nba/master.docx" "s3://$BUCKET/$PREFIX/verticals/nba/master.docx" --profile fortress-tools-deployer
aws s3 cp "$TEMPLATES_DIR/verticals/nba/meta.json"   "s3://$BUCKET/$PREFIX/verticals/nba/meta.json"   --profile fortress-tools-deployer
aws s3 cp "$TEMPLATES_DIR/lob-partials/general-liability.docx"    "s3://$BUCKET/$PREFIX/lob-partials/general-liability.docx"    --profile fortress-tools-deployer
aws s3 cp "$TEMPLATES_DIR/lob-partials/workers-compensation.docx" "s3://$BUCKET/$PREFIX/lob-partials/workers-compensation.docx" --profile fortress-tools-deployer
aws s3 cp "$TEMPLATES_DIR/lob-partials/commercial-property.docx"  "s3://$BUCKET/$PREFIX/lob-partials/commercial-property.docx"  --profile fortress-tools-deployer
aws s3 cp "$TEMPLATES_DIR/registry/boilerplate.json" "s3://$BUCKET/$PREFIX/registry/boilerplate.json" --profile fortress-tools-deployer
echo "Done."
