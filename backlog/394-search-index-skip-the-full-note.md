# 394 Search index: skip the full note_text repair pass when nothing needs it

As the user, I want each index sync to stay cheap in a large data folder, so that the repair check for stale search text doesn't read every row every time.

## What

TagIndex.RepairText (src/Context.Core/TagIndex.cs) reads every note_text row once per sync to find unmapped or stale rows, even when nothing changed. Run the full pass only when it can be needed (for example after the database was opened by an older build, detected by a marker or row-count check, or on upgrade), and keep the guarantee that a deleted or sensitive note's text never stays searchable. Add tests for both the skipped and the triggered pass.

## Why

Code and security reviews of job 295 (round 2, nit 1 each): an O(rows) scan per sync grows with the number of notes, but gating it changes behaviour, so it needs its own review.

## Notes

Proposed by job 295-search-index-limits (review-code 1, review-security 1).

Proposed by job 295-search-index-limits (`factory/jobs/295-search-index-limits/`).
