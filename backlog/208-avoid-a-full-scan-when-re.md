# 208 Avoid a full scan when re-indexing one note's text

As a Context user, I want syncing a changed note to stay quick in a large notes folder, so that the search index doesn't slow down every save.

## What

In TagIndex.Sync, `DELETE FROM note_text WHERE note = $note` scans the whole FTS5 table because `note` is UNINDEXED. Add a rowid map (a small table from note name to FTS rowid) or key rows by rowid so a single note's row is deleted directly. Bump ParserVersion if the schema changes, and add tests.

## Why

Each re-parsed note costs a full table scan today; harmless for small folders, but it grows with the number of notes.

## Notes

Proposed by job 170-full-text-search (review-code round 1, nit 1).

Proposed by job 170-full-text-search (`factory/jobs/170-full-text-search/`).
