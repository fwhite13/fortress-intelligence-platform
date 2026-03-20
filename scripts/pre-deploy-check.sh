#!/bin/bash
# Pre-deploy checklist — run before triggering ANY CodeBuild
# Usage: ./scripts/pre-deploy-check.sh

set -e

echo "=== PRE-DEPLOY CHECKLIST ==="

# 1. Verify local branch
BRANCH=$(git rev-parse --abbrev-ref HEAD)
echo "Branch: $BRANCH"
if [ "$BRANCH" != "main" ]; then
  echo "WARNING: Not on main branch!"
fi

# 2. Get local HEAD
LOCAL_HEAD=$(git rev-parse HEAD)
LOCAL_HEAD_SHORT=$(git rev-parse --short HEAD)
echo "Local HEAD: $LOCAL_HEAD_SHORT ($LOCAL_HEAD)"

# 3. Fetch and check remote
git fetch origin main --quiet
REMOTE_HEAD=$(git rev-parse origin/main)
REMOTE_HEAD_SHORT=$(git rev-parse --short origin/main)
echo "Remote HEAD (origin/main): $REMOTE_HEAD_SHORT"

# 4. Verify they match
if [ "$LOCAL_HEAD" != "$REMOTE_HEAD" ]; then
  echo ""
  echo "ERROR: Local HEAD does not match origin/main!"
  echo "  Local:  $LOCAL_HEAD_SHORT"
  echo "  Remote: $REMOTE_HEAD_SHORT"
  echo ""
  echo "Run: git push origin main"
  echo "Then re-run this script."
  exit 1
fi

echo ""
echo "✅ HEAD matches origin/main ($LOCAL_HEAD_SHORT)"
echo "✅ Safe to trigger CodeBuild"
echo ""
echo "Expected commit in CodeBuild logs: $LOCAL_HEAD_SHORT"
echo ""
