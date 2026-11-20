# 354 Merge notes: refuse the delete when the second history save fails

As a desktop user, I want a merge to keep the merged note when its last history copy can't be saved, so that an edit made on disk during the merge is never lost.

## What

DesktopActions.MergeNotesNow keeps the source in history a second time after the target is saved, and swallows a failure of that second HistoryStore.Keep. Make a failed second Keep stop before the source is deleted, with its own result and message, and add a test for it.

## Why

Code and security reviews of job 268: if the source changed on disk after it was first read and the second Keep fails, the delete still runs and that edit has no saved version.

## Notes

Proposed by job 268-similar-notes (review-code 1, review-security 1).

Proposed by job 268-similar-notes (`factory/jobs/268-similar-notes/`).
