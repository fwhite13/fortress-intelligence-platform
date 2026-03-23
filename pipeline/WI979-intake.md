# WI#979 — Tasks: Assign Task to Another User

## Type
Feature

## Source
Fred White (2026-03-20 8:40 PM)

## Description
Task creation should support assigning a task to another user, not just yourself. For now, no permission enforcement — any user can assign to any user. Eventually will be gated by user permissions.

## Expected Behavior
- Add "Assign to" selector in the task creation dialog
- Options: "Myself" (default) / "Someone else"
- When "Someone else" is selected: show a text input with autocomplete that pulls from the user list
- Autocomplete should search by name/email
- Task is saved with the selected user's ID as `OwnerUserId`
- Task appears in the assignee's Task Center

## Current Behavior
Tasks are always created for the currently logged-in user only. No way to assign to another user.

## Notes
- No permission enforcement for now — any authenticated user can assign to any other user
- Future: `canAssignToOthers` permission gate
- User list source: pull from existing Entra/Cognito user directory or local user table
- Default to "Myself" to avoid accidental mis-assignments
