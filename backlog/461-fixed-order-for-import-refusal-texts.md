# 461 Fixed order for import refusal texts

As a user importing a file, I want the most useful refusal text when several checks fail, so that I know what to fix first.

Category: ux

## What

In `DesktopActions.ImportNoteNow` the `.md` check sits in the same try block as the title normalization, so which refusal text shows depends on check order. Decide the order (for example `.md` first, then title, then size and type) and test a file that fails several checks.

## Why

Review-ux round 1 of job 385 noted it; the spec's cases all hit the `.md` check first, so it was left.

## Notes

Proposed by job 385-import-refusals-safe-save (`factory/jobs/385-import-refusals-safe-save/`).
