# Hawkeye Cycle 2 Fast-Verify — ADO#3093 Runtime Preference Detection

## Commit to verify: 12e25d3d

## Working directory: /home/fredw/projects/fip/fait-v2

## Three items to verify:

### 1. hasPreferenceSignal / firePreferenceWrite in Bedrock streaming path
Check lines ~1395-1396 in agent-harness/harness-server.js (or wherever the Bedrock streaming path is).
Confirm that `hasPreferenceSignal` and `firePreferenceWrite` are actually CALLED (not just defined).
Show the exact lines with context.

### 2. Startup warning for INTERNAL_API_TOKEN
Find the exact string: `console.warn('[harness] WARNING: INTERNAL_API_TOKEN not set — preference writes will fail with 401')`
Confirm it is present inside the bootstrap IIFE or startup code.
Show the exact lines with context.

### 3. Syntax check
Run: node --check agent-harness/harness-server.js
Report pass or fail.

## Steps:
1. Read lines 1385-1410 of agent-harness/harness-server.js to see the Bedrock streaming path
2. Search for `hasPreferenceSignal` and `firePreferenceWrite` to confirm they are called (not just defined)
3. Search for `INTERNAL_API_TOKEN` warning string
4. Run node --check agent-harness/harness-server.js
5. Report findings for all three items
