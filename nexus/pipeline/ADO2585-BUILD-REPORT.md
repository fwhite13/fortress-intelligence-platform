# Build Report: ADO#2585 — Two-Call Bedrock TC Architecture

**Commit:** 83dbdfa
**Date:** 2026-04-30

## Changes Made
### ArtifactGenerationService.cs
- Replaced single Bedrock call (maxTokens=8192) with two-call flow (maxTokens=32768 each)
- Removed inline C# TC generation (_wiClassifier.ShouldGenerateTestCases loop)
- Added Call 2: TC compliance scan using TcScanSystem prompt
- Added ParseTcScanResult() method + TcScanResult/TcParentUpdate DTOs
- Call 2 failure is non-fatal: logs warning, returns Call 1 result unmodified

### appsettings.json
- ArtifactGenSystem: stripped TC sections per spec S3 (old basic prompt replaced with stripped v6)
- TcScanSystem: added (full prompt from spec S2)

### appsettings.Production.json
- ArtifactGenSystem: added (stripped v6 prompt — was missing from prod config)
- TcScanSystem: added (full prompt from spec S2)

## Acceptance Criteria
- [x] AC-1: Call 1 output has zero Test Case items (verified by code inspection — ParseWorkItems returns only what Bedrock returns, TCs are not in Call 1 prompt)
- [x] AC-7: ArtifactGenSystem prompt has no TC references (verified by string search: Test Case, Rule A, Rule B, testedByTitles, MANDATORY SECOND PASS, test-case — all absent)

## Self-Review Checklist
- [x] BuildSucceeded (dotnet build — 0 errors, 1 pre-existing warning)
- [x] Old _wiClassifier.ShouldGenerateTestCases loop removed
- [x] Both appsettings files updated
- [x] TcScanSystem prompt JSON-escaped correctly (validated with json.load)
- [x] ParseTcScanResult handles empty testCases without throwing (TryGetProperty guards)
- [x] Spec S8 respected: no other changes outside listed files
