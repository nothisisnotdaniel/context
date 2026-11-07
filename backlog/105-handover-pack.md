# 105 Handover pack

As a developer going on holiday or switching projects, I want a handover document built from my notes, todos and hours, so that a colleague can take over without a long meeting.

## What

`context handover --items <number,...> | --since <date> [--out <file>]` writes a Markdown document with one section per work item: latest notes, decisions, open todos, booked hours and recent commits, plus an empty Next steps section to fill in. Notes tagged `#private` are left out. The file is escaped and contains no scripts. Full path.

## Why

Handovers are a recurring, rushed chore before every holiday. Having the material already collected turns an evening of writing into a review.
