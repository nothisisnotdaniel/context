# 351 /standup opens today's note before writing

As a desktop user, I want /standup to open today's note first and then add the standup, so that I see the section appear in the note I'm looking at, as /today does.

Category: bug

## What

When today's note is not open, /standup in Main.razor writes the ## Standup section first and opens the note afterwards. The spec said it opens today's note first, like /today. Change the order without bringing back the race with the editor mount that the builder avoided (see job 265's build.md, Round 1).

## Why

UX review of job 265: the order differs from /today and from the spec; the builder kept it to avoid a race, so this needs its own careful fix.

## Notes

Proposed by job 265-standup-notes (review-ux 1).

Proposed by job 265-standup-notes (`factory/jobs/265-standup-notes/`).
