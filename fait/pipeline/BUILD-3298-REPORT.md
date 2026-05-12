# Build Report — ADO#3298

## What was built
Added a non-root user (`harness`) to the agent-harness Docker container. Claude Code CLI hardcodes a safety check refusing `--dangerously-skip-permissions` when running as root — this was the root cause of all CC spawn failures.

## Files changed
- `fait-v2/agent-harness/Dockerfile` — Added `groupadd/useradd` for `harness` user, `chown -R harness /app`, `ENV PATH` to include `/usr/local/bin`, `USER harness` before `CMD`

## Parallelization used
No — #3298 and #3299 were run sequentially (both touch the same repo).

## CC sessions run
1 CC Sonnet session. Brief piped via `cat /tmp/brief-3298.md | claude --model sonnet --print --dangerously-skip-permissions`.

## Acceptance criteria verification
- [x] `USER harness` directive before `CMD` — line 63 in updated Dockerfile
- [x] Non-root user `harness` created with home `/home/harness` — `useradd -m -d /home/harness harness`
- [x] npm global binaries accessible — `ENV PATH="/usr/local/bin:/usr/local/lib/node_modules/.bin:${PATH}"`
- [x] `/app` owned by harness — `RUN chown -R harness:harness /app`
- [x] `node --check harness-server.js` — PASSED (JS file untouched in this commit)

## Known edge cases / things Clint should scrutinize
- The `/workspace` EFS mount point is created as root before the USER switch. EFS mount perms depend on the EFS access point configuration — if EFS AP is configured with UID 0 (root), the harness user won't be able to write there. **May need EFS access point to use UID matching harness user or world-writable.** Worth verifying in ECS task config.
- `/tmp` is sticky bit / world-writable, so brief files written there by the harness process are fine.
- npm global dirs (`/usr/local/lib/node_modules`) are installed as root — harness user can EXECUTE but not install. This is correct behavior.

## How to test locally
```bash
cd /home/fredw/projects/fip/fait-v2/agent-harness
docker build -t harness-test .
docker run --rm harness-test id  # Should show uid=999(harness)
docker run --rm harness-test which claude  # Should show /usr/local/bin/claude
```

## Commit
`096bad36` — `fix(fait#3298): run harness as non-root user — unblocks CC --dangerously-skip-permissions`
