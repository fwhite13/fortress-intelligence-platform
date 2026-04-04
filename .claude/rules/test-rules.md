# Test Rules

## Build Gate (MANDATORY before every commit)
`dotnet build` must pass with **0 errors** before any commit.

## Per-Service Build Commands
| Service | Command |
|---------|---------|
| FAIT | `dotnet build` in `fait/src/` |
| FIRM | `dotnet build` in `firm/src/` |
| NEXUS | `dotnet build` in `nexus/src/` |
| FAMOS | `dotnet build` in `famos/src/` |
| FORMS | `dotnet build` in `forms/src/` |

## TypeScript / Node
- For vpbot or any TypeScript service: run `npm run build` before Docker build
- Fix all TypeScript errors before committing

## Shell Scripts
- Any new or modified `.sh` script: run `bash -n script.sh` to verify syntax
- Do NOT commit shell scripts with syntax errors

## dotnet test
- No test suite currently required — `dotnet build` is the gate
- Do NOT run `dotnet test` (no test project configured)
- This rule will be updated when a test suite is added

## Build Failure Protocol
- If build fails: fix errors, do NOT commit broken code
- Report build failure in ADO comment with error summary
- Do NOT push to main with failing build
