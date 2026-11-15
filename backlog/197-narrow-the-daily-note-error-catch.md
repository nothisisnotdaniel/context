# 197 Narrow the daily-note error catch in the desktop app

As a Context developer, I want the desktop daily note to catch only file errors, so that a real bug shows up instead of being reported as a write failure.

## What

In DesktopActions.OpenDailyNoteNow, catch only IOException and UnauthorizedAccessException (as the CLI `context today` does) instead of every Exception. Check whether the other DesktopActions catch-alls should follow, and keep the fixed error text.

## Why

A catch-all turns any bug into "Can't write <name>. Nothing was done.", which hides the real cause and differs from the CLI.

## Notes

Proposed by job 164-daily-note (review-code round 1, nit 2).

Proposed by job 164-daily-note (`factory/jobs/164-daily-note/`).
