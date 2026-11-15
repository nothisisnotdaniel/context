# 198 Say when today's note already exists

As a Context user, I want a short status line when /today opens an existing daily note, so that I know nothing new was created.

## What

When /today (desktop) opens a daily note that already exists, show a grey status line such as "Opened <name>" instead of nothing. Keep the accent "Created <name>" line for a new note. Update the spec text and tests.

## Why

Today an existing note opens silently, so it is unclear whether /today did anything or created the page.

## Notes

Proposed by job 164-daily-note (review-ux round 1, nit 1).

Proposed by job 164-daily-note (`factory/jobs/164-daily-note/`).
