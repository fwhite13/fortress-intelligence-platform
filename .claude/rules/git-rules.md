# Git Rules

## Staging
- ALWAYS use `git add -A` before commit — never partial adds
- Partial adds (`git add path/to/file`) are prohibited unless explicitly specified in WI

## Commit Message Format
```
feat|fix|chore(scope#ADO-ID): description
```
Examples:
- `feat(fait#1234): add Bedrock streaming support`
- `fix(firm#1501): correct Teams webhook URL config`
- `chore(pipeline#1588): add CLAUDE.md modular rule files`

## Push
- ALWAYS push to `origin/main` after every commit: `git push origin main`
- Never commit without pushing in the same session

## Forbidden Operations
- NEVER `git reset --hard` without explicit pre-authorization in the WI
- NEVER `git push --force` without explicit pre-authorization in the WI
- NEVER amend published (pushed) commits
- NEVER rebase published commits

## Verification
- After push: run `git log --oneline -3` to confirm commit appears
- Include the commit hash in the ADO comment and Build Report
