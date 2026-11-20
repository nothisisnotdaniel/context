# 356 Merge notes: re-check subpages right before the delete

As a desktop user, I want a merge to refuse to delete a note that gained a subpage during the merge, so that a subpage is never removed with it.

Category: bug

## What

MergeNotesNow checks Descendants(merged.Name) > 0 well before Delete, which removes the whole subtree. Re-check just before Delete and refuse (keeping the merged target) if a subpage appeared, with a message and a test.

## Why

Security review of job 268: a subpage created in between (for example by another app or sync) would be deleted with the source.

## Notes

Proposed by job 268-similar-notes (review-security 3).

Proposed by job 268-similar-notes (`factory/jobs/268-similar-notes/`).
