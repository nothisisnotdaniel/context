# 459 Close the sensitive-note race in create-only import save

As a user importing a note, I want a sensitive note created at the same moment to always block the import, so that no plain copy ends up next to it.

Category: security

## What

In `NoteStore.Save` (create-only mode), the sensitive-name re-check after `BeforeSaveMove` and the `File.Move` are still two steps, so a `<title>.ctxs` created between them is not caught. Close that window (for example with a lock around the check and the move, or by checking again after the move and undoing it).

## Why

Review-security round 1 of job 385 noted the remaining window. The spec accepted it and nothing is overwritten, so it was left for its own job.

## Notes

Proposed by job 385-import-refusals-safe-save (`factory/jobs/385-import-refusals-safe-save/`).
