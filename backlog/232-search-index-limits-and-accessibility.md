# 232 Search index limits and accessibility

As a Context user, I want search and the tag and link index to stay fast and use little memory with many notes, and the result count to be announced, so that search works well in a large data folder.

## What

- Index limits (was #29): `TagIndex.Sync` reads each changed note whole with no size limit; skip (or only partly read) notes above a sensible size and say so in `/tags`. `TagIndex.Links` reads the whole `note_links` table on every `/links` call; query only the rows for that note and the rows whose target matches it.
- Base the search index size cap on the text actually read (was #206): In TagIndex.Sync, check the MaxEditBytes cap against the text returned by store.Read (for example Encoding.UTF8.GetByteCount of it) instead of a separate FileInfo.Length taken after the read. Define how a BOM or invalid UTF-8 counts, and add tests for a note at the boundary.
- Limit memory used by a broad full-text search (was #207): In TagIndex.Search, avoid loading the full body of every matching row: cap the rows read (the panel already shows at most 100), or fetch only what FirstLine needs. Keep the result order and the "… n more" count correct. Add a test with many matching notes.
- Avoid a full scan when re-indexing one note's text (was #208): In TagIndex.Sync, `DELETE FROM note_text WHERE note = $note` scans the whole FTS5 table because `note` is UNINDEXED. Add a rowid map (a small table from note name to FTS rowid) or key rows by rowid so a single note's row is deleted directly. Bump ParserVersion if the schema changes, and add tests.
- Announce the search result count to screen readers (was #209): In SearchPanel.razor, add a role=status line (or aria-live) with the number of hits, for example "12 notes found", and label the hit list. Update the spec text and tests.

## Why

- #29: `TagIndex.Sync` reads every changed note whole, and `/links` reads the whole links table each time. Limits and targeted queries keep both fast.
- #206: The cap is checked with the file length after the whole file was read, so a file that grows in between can put more than the cap into note_text, and one that shrinks can lose its row.
- #207: A 3-character query like "the" can match every note, so memory use can grow to note count × MaxEditBytes.
- #208: Each re-parsed note costs a full table scan today; harmless for small folders, but it grows with the number of notes.
- #209: The hit list is a bare list, so a screen-reader user doesn't learn the result count without moving through it.

## Notes

- Merges #29, #206, #207, #208, #209 on 2026-11-16, to save the fixed cost of one job per item.
- Use quality mode. Bump ParserVersion once if the schema changes.
- #206: Proposed by job 170-full-text-search (review-security round 1, nit 1). Proposed by job 170-full-text-search (`factory/jobs/170-full-text-search/`).
- #207: Proposed by job 170-full-text-search (review-security round 1, nit 2). Proposed by job 170-full-text-search (`factory/jobs/170-full-text-search/`).
- #208: Proposed by job 170-full-text-search (review-code round 1, nit 1). Proposed by job 170-full-text-search (`factory/jobs/170-full-text-search/`).
- #209: Proposed by job 170-full-text-search (review-ux round 1, nit 1). Proposed by job 170-full-text-search (`factory/jobs/170-full-text-search/`).
