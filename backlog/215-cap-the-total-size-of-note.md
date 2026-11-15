# 215 Cap the total size of note history

As a Context user, I want all note history together to stay within a size limit, so that many notes with history can't fill my disk.

## What

In NoteHistory, add a cap across all `.history/` folders (or per notes folder) on top of the per-note caps, removing the oldest versions first when it is passed. Decide whether it is fixed or a setting, and add tests.

## Why

The caps apply per note today, so a large number of notes can still use a lot of disk in total.

## Notes

Proposed by job 174-note-history (review-security round 1, nit 2).

Proposed by job 174-note-history (`factory/jobs/174-note-history/`).
