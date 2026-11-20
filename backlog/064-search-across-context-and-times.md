# 064 Search across Context and Times

As a Context and Times user, I want search to find Times checkpoints as well as notes, so that "#1234 in October" shows both the notes and the hours.

## What

Full-text search also finds Times checkpoints (name, work item, date and duration, read-only from Times' day files), so "#1234 in October" shows the notes and the time bookings together, with a date range filter. Builds on full-text search.

## Why

Notes and time bookings describe the same work, but today they're searched separately.

## Notes

- Search is opened from a magnifying-glass icon button in the sidebar header (next to the Triage, Tags and Settings icon buttons), not only from the palette's `/search`. If the job touches how search is opened and that button doesn't exist yet, add it. Requested by the user on 2026-11-16.
