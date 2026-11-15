# 206 Base the search index size cap on the text actually read

As a Context user, I want the search index to apply its size cap to the note text it actually read, so that a note changing during a sync can't slip past the cap or lose its row.

## What

In TagIndex.Sync, check the MaxEditBytes cap against the text returned by store.Read (for example Encoding.UTF8.GetByteCount of it) instead of a separate FileInfo.Length taken after the read. Define how a BOM or invalid UTF-8 counts, and add tests for a note at the boundary.

## Why

The cap is checked with the file length after the whole file was read, so a file that grows in between can put more than the cap into note_text, and one that shrinks can lose its row.

## Notes

Proposed by job 170-full-text-search (review-security round 1, nit 1).

Proposed by job 170-full-text-search (`factory/jobs/170-full-text-search/`).
