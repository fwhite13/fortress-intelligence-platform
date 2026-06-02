# CC Task: ADO#4832 — Gate Asks User for Output Path in Chat

## Problem
During task gate assessment and CC task brief, the assistant sometimes asks the user to specify an output path or filename in chat. This should not happen — the folder picker already handles working folder selection, and the working folder path is already injected into the CC context.

## File to modify
`/home/fredw/projects/fip/fait/agent-harness/harness-server.js`

## Changes Required

### 1. Gate system prompt — add working folder instruction

In `harness-server.js`, find the gate system prompt construction around line 3101. There is this block:
```
gateSystemParts.push(`[TASK GATE — read carefully]
You are the FAIT assistant...
```

Add this instruction to the end of that gate system prompt string (inside the backtick template literal, before the closing backtick):

```
Do not ask the user about output paths, filenames, or where to save files. The user has already selected a working folder. All files must be written to the working folder — this is handled automatically and does not require user input.
```

### 2. Artifact Generation Rules context — reinforce working folder instruction

In `harness-server.js`, find the `## Artifact Generation Rules` section that is pushed into `contextParts` (around line 3623):
```
contextParts.push(`## Artifact Generation Rules
When the user asks for a file, generate a real file...
```

After the sentence `After creating a file, confirm its name and location in your response.`, add:
```
Do not ask the user where to save files — always write to the working folder path provided. Do not ask for output paths, filenames, or save locations. The working folder is your output directory.
```

### 3. EXECUTE_DIRECTIVE — add explicit no-ask instruction

Find the `EXECUTE_DIRECTIVE` constant (around line 3731):
```
const EXECUTE_DIRECTIVE = `YOUR ONLY JOB IS TO EXECUTE THE FOLLOWING TASK RIGHT NOW.
DO NOT narrate what you will do...`
```

Add to EXECUTE_DIRECTIVE:
```
Do NOT ask the user where to save output files — write all output to the working folder specified below.
```

## Important: Do not change any other logic
- Do not change folder picker logic
- Do not change the harness POST endpoints
- Do not change the CC spawn configuration
- Only modify the text of the gate system prompt, artifact generation rules, and execute directive strings

## Acceptance Criteria
- AC1: After folder selection, CC task does not ask user for output path or filename in chat
- AC2: Task writes output to working folder without prompting
- AC3: Working folder path is correctly included in gate prompt context (already handled by existing Working Folder context section — just verify it's there)
