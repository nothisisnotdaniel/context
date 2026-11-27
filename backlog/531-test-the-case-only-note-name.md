# 531 Test the case-only note-name tie-break of person note todos

As a maintainer, I want the ordinal tie-break between note names that differ only in case to be tested, so that the order stays stable.

Category: test

## What

Add a test for the AC-3 ordinal tie-break (note A vs a) in PersonView note todos, for example with an in-memory index rather than files, so it runs on case-insensitive file systems.

## Why

review-code 1 on job 487: the tie-break is untested because two such files collide on case-insensitive file systems.

## Notes

Proposed by job 487-person-note-todos.67b86604ce9643509d12bed49684c969 (`factory/jobs/487-person-note-todos.67b86604ce9643509d12bed49684c969/`).
