# 088 Vacation handover

As a Context and Times user, I want a handover note drafted before my absence, so that my colleagues get what they need without me compiling it by hand.

Category: feature

## What

Before absence days booked in Times (read-only from Times' files), Context offers to create a handover note listing open todos, waiting-for items, meetings during the absence and the project pages of work items booked in the last two weeks, for the user to edit and share. Also a button and a CLI command for any date range. Builds on Times' absence days, the waiting-for list and project pages. Also a handover by work item: `context handover --items <number,...> | --since <date> [--out <file>]` writes a Markdown document with one section per work item (latest notes, decisions, open todos, booked hours and recent commits) and an empty Next steps section. Notes tagged `#private` are left out, and the file is escaped and contains no scripts.

## Why

Before a vacation, collecting open todos, waiting-for items, meetings and active projects takes time when it's scarcest. A draft I edit and share saves most of it.

## Notes

Merged on 2026-11-15: #105 (Handover pack).
