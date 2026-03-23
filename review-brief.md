# Review Brief: WI#918 — CodeBuild Audit Trail Fix

## Context
Reviewer: Hawkeye (Clint Barton). Reviewing commit d40a8ac in ~/projects/fip/.
Task: All 6 buildspecs received two audit lines in pre_build, and a new pre-deploy-check.sh was created.

## Files Reviewed (content provided below)

---

### famos/buildspec.yml
```yaml
version: 0.2

phases:
  pre_build:
    commands:
      - echo Logging in to Amazon ECR...
      - aws ecr get-login-password --region $AWS_DEFAULT_REGION | docker login --username AWS --password-stdin $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com
      - IMAGE_TAG=${CODEBUILD_RESOLVED_SOURCE_VERSION:-latest}
      - echo "Building commit:" && git log -1 --oneline
      - echo "IMAGE_TAG=$IMAGE_TAG"
  build:
    commands:
      - echo Build started on `date`
      - docker build -f famos/Dockerfile -t famos-web:$IMAGE_TAG .
      - docker tag famos-web:$IMAGE_TAG $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com/famos-web:dev-latest
  post_build:
    commands:
      - docker push $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com/famos-web:dev-latest
      - docker tag famos-web:$IMAGE_TAG $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com/famos-web:latest
      - docker push $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com/famos-web:latest
      - aws ecs update-service --cluster fortress-tools-cluster --service famos-dev --force-new-deployment --region $AWS_DEFAULT_REGION
      - echo Deploy triggered

env:
  variables:
    AWS_DEFAULT_REGION: us-east-1
    AWS_ACCOUNT_ID: 742932328420
```

---

### firm/buildspec.yml
```yaml
version: 0.2

phases:
  pre_build:
    commands:
      - echo Logging in to Amazon ECR...
      - aws ecr get-login-password --region $AWS_DEFAULT_REGION | docker login --username AWS --password-stdin $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com
      - IMAGE_TAG=${CODEBUILD_RESOLVED_SOURCE_VERSION:-latest}
      - echo "Building commit:" && git log -1 --oneline
      - echo "IMAGE_TAG=$IMAGE_TAG"
  build:
    commands:
      - echo Build started on `date`
      - echo Building Docker image...
      - docker build -f firm/Dockerfile.debian -t firm-web:$IMAGE_TAG .
      - docker tag firm-web:$IMAGE_TAG $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com/firm-web:latest
      - docker tag firm-web:$IMAGE_TAG $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com/firm-web:$IMAGE_TAG
  post_build:
    commands:
      - echo Build completed on `date`
      - echo Pushing Docker image...
      - docker push $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com/firm-web:latest
      - docker push $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com/firm-web:$IMAGE_TAG
      - echo Updating ECS service...
      - aws ecs update-service --cluster fortress-tools-cluster --service firm-web --force-new-deployment --region $AWS_DEFAULT_REGION
      - echo Deploy triggered

env:
  variables:
    AWS_DEFAULT_REGION: us-east-1
    AWS_ACCOUNT_ID: 742932328420
```

---

### fait/buildspec.yml
```yaml
version: 0.2

phases:
  pre_build:
    commands:
      - echo Logging in to Amazon ECR...
      - aws ecr get-login-password --region $AWS_DEFAULT_REGION | docker login --username AWS --password-stdin $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com
      - IMAGE_TAG=${CODEBUILD_RESOLVED_SOURCE_VERSION:-latest}
      - echo "Building commit:" && git log -1 --oneline
      - echo "IMAGE_TAG=$IMAGE_TAG"
  build:
    commands:
      - echo Build started on `date`
      - echo Building Docker image...
      - docker build -f fait/Dockerfile -t fred-chat:$IMAGE_TAG .
      - docker tag fred-chat:$IMAGE_TAG $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com/fred-chat:kb-latest
      - docker tag fred-chat:$IMAGE_TAG $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com/fred-chat:$IMAGE_TAG
  post_build:
    commands:
      - echo Build completed on `date`
      - echo Pushing Docker image...
      - docker push $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com/fred-chat:kb-latest
      - docker push $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com/fred-chat:$IMAGE_TAG
      - echo Updating ECS service...
      - aws ecs update-service --cluster fortress-tools-cluster --service fred-dev --force-new-deployment --region $AWS_DEFAULT_REGION
      - echo Deploy triggered

env:
  variables:
    AWS_DEFAULT_REGION: us-east-1
    AWS_ACCOUNT_ID: 742932328420
```

---

### forms/buildspec.yml
```yaml
version: 0.2

phases:
  pre_build:
    commands:
      - echo Logging in to Amazon ECR...
      - aws ecr get-login-password --region $AWS_DEFAULT_REGION | docker login --username AWS --password-stdin $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com
      - IMAGE_TAG=${CODEBUILD_RESOLVED_SOURCE_VERSION:-latest}
      - echo "Building commit:" && git log -1 --oneline
      - echo "IMAGE_TAG=$IMAGE_TAG"
  build:
    commands:
      - echo Build started on `date`
      - echo Building Docker image...
      - docker build -f Dockerfile -t formiq:$IMAGE_TAG .
      - docker tag formiq:$IMAGE_TAG $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com/formiq:dev-latest
  post_build:
    commands:
      - echo Build completed on `date`
      - echo Pushing Docker image...
      - docker push $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com/formiq:dev-latest
      - echo Updating ECS service...
      - aws ecs update-service --cluster fortress-tools-cluster --service formiq-dev --force-new-deployment --region $AWS_DEFAULT_REGION
      - echo Deploy triggered

env:
  variables:
    AWS_DEFAULT_REGION: us-east-1
    AWS_ACCOUNT_ID: 742932328420
```

---

### cowork/buildspec.yml
```yaml
version: 0.2
phases:
  pre_build:
    commands:
      - aws ecr get-login-password --region us-east-1 | docker login --username AWS --password-stdin 742932328420.dkr.ecr.us-east-1.amazonaws.com
      - COMMIT_HASH=$(echo $CODEBUILD_RESOLVED_SOURCE_VERSION | cut -c 1-7)
      - IMAGE_TAG=${COMMIT_HASH:=latest}
      - echo "Building commit:" && git log -1 --oneline
      - echo "IMAGE_TAG=$IMAGE_TAG"
  build:
    commands:
      - docker build -f cowork/Dockerfile.web -t cowork-web .
      - docker build -f cowork/Dockerfile.agent -t cowork-agent .
      - docker tag cowork-web:latest   742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-web:$IMAGE_TAG
      - docker tag cowork-agent:latest 742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-agent:$IMAGE_TAG
      - docker tag cowork-web:latest   742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-web:latest
      - docker tag cowork-agent:latest 742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-agent:latest
  post_build:
    commands:
      - docker push 742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-web:$IMAGE_TAG
      - docker push 742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-agent:$IMAGE_TAG
      - docker push 742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-web:latest
      - docker push 742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-agent:latest
      - printf '[{"name":"cowork-web","imageUri":"%s"},{"name":"cowork-agent","imageUri":"%s"}]' 742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-web:$IMAGE_TAG 742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-agent:$IMAGE_TAG > imagedefinitions.json
artifacts:
  files: imagedefinitions.json
```

---

### mcp-memory/buildspec.yml
```yaml
version: 0.2

env:
  variables:
    AWS_ACCOUNT_ID: '742932328420'

phases:
  pre_build:
    commands:
      - aws ecr get-login-password --region us-east-1 | docker login --username AWS --password-stdin $AWS_ACCOUNT_ID.dkr.ecr.us-east-1.amazonaws.com
      - IMAGE_TAG=$(echo $CODEBUILD_RESOLVED_SOURCE_VERSION | cut -c1-7)
      - ECR_URI=$AWS_ACCOUNT_ID.dkr.ecr.us-east-1.amazonaws.com/mcp-memory
      - echo "Building commit:" && git log -1 --oneline
      - echo "IMAGE_TAG=$IMAGE_TAG"
  build:
    commands:
      - cd mcp-memory
      - docker build -t $ECR_URI:$IMAGE_TAG -t $ECR_URI:latest .
  post_build:
    commands:
      - docker push $ECR_URI:$IMAGE_TAG
      - docker push $ECR_URI:latest
      - aws ecs update-service --cluster fortress-tools-cluster --service mcp-memory --force-new-deployment --region us-east-1
      - printf '[{"name":"mcp-memory","imageUri":"%s"}]' $ECR_URI:$IMAGE_TAG > imagedefinitions.json

artifacts:
  files:
    - imagedefinitions.json
```

---

### scripts/pre-deploy-check.sh
```bash
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
```

File permissions: -rwxrwxr-x (executable ✅)

---

## Review Task

Carefully analyze all content above and provide a structured code review answering each question below.

### P1 Checks

1. **git log line present in all 6 buildspecs?**
   - Check each: famos, firm, fait, forms, cowork, mcp-memory
   - Must have: `- echo "Building commit:" && git log -1 --oneline`
   - Must be inside `pre_build.commands` with proper 6-space indentation (dash + space = 6 chars total)

2. **echo IMAGE_TAG line present in all 6 buildspecs?**
   - Check each: famos, firm, fait, forms, cowork, mcp-memory
   - Must have: `- echo "IMAGE_TAG=$IMAGE_TAG"`
   - Must be inside `pre_build.commands` with proper 6-space indentation

3. **pre-deploy-check.sh correctness:**
   - Does it call `git fetch origin main` before comparison? YES/NO
   - Does it compare `git rev-parse HEAD` vs `git rev-parse origin/main` (full SHA, not branch names)? YES/NO
   - Does it exit with code 1 on mismatch? YES/NO (check for `exit 1`)
   - Is it executable? YES (confirmed -rwxrwxr-x)

4. **No other files modified:** The git diff shows: cowork/buildspec.yml, fait/buildspec.yml, famos/buildspec.yml, firm/buildspec.yml, forms/buildspec.yml, mcp-memory/buildspec.yml, pipeline/WI918-BUILD-REPORT.md, scripts/pre-deploy-check.sh. The BUILD-REPORT.md addition is expected pipeline artifact. Confirm: no Dockerfiles, no source code, no .env files touched.

5. **YAML validity for all 6:** Confirm indentation is consistent. In YAML CodeBuild buildspecs, commands under pre_build are typically indented 6 spaces (2 for phases, 2 for pre_build, 2 for commands key, then items under commands use 6 spaces + dash). Verify the new lines match the surrounding lines' indentation exactly.

### P2 Checks

6. **mcp-memory special structure:** It defines `ECR_URI` variable AFTER `IMAGE_TAG`. Confirm the git log and echo lines are placed after IMAGE_TAG (position 4 and 5 in commands list) and not before IMAGE_TAG is defined.

7. **cowork COMMIT_HASH:** cowork uses `COMMIT_HASH` then `IMAGE_TAG=${COMMIT_HASH:=latest}`. The echo line is `echo "IMAGE_TAG=$IMAGE_TAG"` — confirm this is correct (prints the actual tag being used for docker operations, which is $IMAGE_TAG).

### Final Output Format
Provide:
- VERDICT: PASS or NEEDS-CHANGES
- Per-file findings table (file | git_log_line | echo_line | indentation | notes)
- pre-deploy-check.sh findings (each criterion yes/no)
- Any issues found (categorized Critical / Important / Nitpick)
- Overall summary (2-3 sentences)
