# 363 Calendar: skip symlinked .ics files

As the user, I want Context to skip a calendar/*.ics file that is a symlink, so that calendar reads follow the same link rule as notes and never read a file outside my data folder.

Category: security

## What

CalendarStore (src/Context.Core/CalendarStore.cs) now reads calendar/*.ics through BoundedFile, size-capped and FIFO-safe, but a symlinked .ics pointing outside the root is still read. Apply the same IsLink filter NoteStore.Files uses when listing calendar files, and add a test with a linked .ics that is skipped.

## Why

Security review of job 275 (nit 2): the content is only parsed as events and never echoed raw, so the risk is low, but the rule is inconsistent with notes.

## Notes

Proposed by job 275-bounded-regular-file-reads (review-security 2).

Proposed by job 275-bounded-regular-file-reads (`factory/jobs/275-bounded-regular-file-reads/`).
