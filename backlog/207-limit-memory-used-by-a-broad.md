# 207 Limit memory used by a broad full-text search

As a Context user, I want a short, common search term to stay fast and light, so that searching a large notes folder doesn't load every note into memory.

## What

In TagIndex.Search, avoid loading the full body of every matching row: cap the rows read (the panel already shows at most 100), or fetch only what FirstLine needs. Keep the result order and the "… n more" count correct. Add a test with many matching notes.

## Why

A 3-character query like "the" can match every note, so memory use can grow to note count × MaxEditBytes.

## Notes

Proposed by job 170-full-text-search (review-security round 1, nit 2).

Proposed by job 170-full-text-search (`factory/jobs/170-full-text-search/`).
