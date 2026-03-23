# WI#977 — Task Creation Broken (Cannot Create Task)

## Type
Bug

## Source
Lauren Williams feedback (email 2026-03-20 5:52 PM)

## Description
Lauren attempted to create a task in the Task Center and it didn't work.

## Expected Behavior
User fills out task form, submits, task appears in Task Center list.

## Current Behavior
Task creation fails or task does not appear after creation. (Related known issue: WI#941 — `GetOpenTasksForUserAsync` filters by `OwnerUserId == userId` but userId format mismatch between email and Entra OID means tasks save to DB but don't appear in the list.)

## Notes
- WI#941 root cause: task DOES save to DB (`8e351a58-94e7-4bb2-9f90-920124e3953e` confirmed) but doesn't display due to userId format mismatch
- This WI is the user-facing symptom; WI#941 is the code fix
- Verify: after WI#941 fix is deployed, confirm Lauren (or any user) can create a task and see it appear
