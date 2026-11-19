# 355 Merge notes: clearer message when a step after the save fails

As a desktop user, I want a merge that fails after the target was saved to tell me what was done and what wasn't, so that I don't think nothing was written.

## What

In DesktopActions.MergeNotesNow the outer catch turns an exception from notes.Delete or ChangeRecent, which run after the target is saved, into "Can't write …". Give that case its own message saying the target was merged but the source could not be removed, and add a test.

## Why

Security review of job 268: the current text is misleading, since the target already holds the merged text.

## Notes

Proposed by job 268-similar-notes (review-security 2).

Proposed by job 268-similar-notes (`factory/jobs/268-similar-notes/`).
