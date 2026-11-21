# 460 Refuse case-only clashes on case-sensitive file systems in import save

As a user on a case-sensitive file system, I want an import to refuse when a note differing only in case appears, so that I don't get two notes that differ only in case.

Category: security

## What

In create-only `NoteStore.Save`, `File.Move(overwrite: false)` does not fail on a case-sensitive file system when `plan.md` appears while the title is `Plan`, so `Plan.md` is created next to it. Check for a case-insensitive clash right before the move as well and refuse it as `NoteTakenException`.

## Why

Review-security round 1 of job 385 found it. Nothing is lost, but it changes behaviour, so it was left for its own job.

## Notes

Proposed by job 385-import-refusals-safe-save (`factory/jobs/385-import-refusals-safe-save/`).
