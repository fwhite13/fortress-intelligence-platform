# Build Report — ADO#2831 Cycle 2

## What was built
One-line fix in `NexusClaimsTransformation.cs`: replaced silent `?? new ClaimsIdentity()` orphaned-identity fallback with `?? throw new InvalidOperationException(...)` to surface auth failures loudly instead of silently dropping role claims.

## Files changed
- `src/FortressNexus.Web/Services/NexusClaimsTransformation.cs` — Line 45–46: `?? new ClaimsIdentity()` → `?? throw new InvalidOperationException("ClaimsIdentity not found on cloned principal — cannot inject NEXUS roles.")`

## Commit
`d22bb64` — `fix(nexus#ADO2831): throw instead of silent orphaned ClaimsIdentity fallback`

## Parallelization used
No — single-file, single-line change.

## CC sessions run
1 — CC Sonnet. Executed cleanly, 0 errors, 1 pre-existing unrelated warning.

## Acceptance criteria verification
- [x] `?? new ClaimsIdentity()` removed from file — verified by read
- [x] `?? throw new InvalidOperationException("ClaimsIdentity not found on cloned principal — cannot inject NEXUS roles.")` present — verified by read
- [x] Build succeeded — 0 errors reported by CC
- [x] No other code touched — confirmed by diff scope

## Known edge cases / things Clint should scrutinize
- The throw path will only trigger if `principal.Clone()` returns a principal whose `.Identity` is null or not a `ClaimsIdentity` — this should never happen in practice under Entra OIDC auth, but if it does, the exception will surface in CloudWatch and the request will 500. This is the correct and desired behavior.
- The pre-existing warning is unrelated to this change.

## How to test locally
1. Run NEXUS locally with a valid Entra-authenticated session
2. Confirm role injection still works (user with roles in `NexusUserRoles` gets them on the principal)
3. To test the throw path: mock `principal.Clone()` to return a principal with a null Identity — confirm `InvalidOperationException` is thrown with the expected message
