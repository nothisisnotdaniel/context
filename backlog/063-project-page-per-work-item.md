# 063 Project page per work item

As a Context and Times user, I want one page per work item with its notes, todos, people, meetings and booked hours, so that I see everything about a project at a glance.

## What

A page for each `#<number>` work item that brings together its notes, todos, people, meetings and the hours booked on it in Times (read from Times' day files, read-only, same rules as Times bookings in /current), per week and in total. Builds on one project list and people pages.

## Why

Project information is spread across notes, todos and Times' day files. Bringing it together, read-only from Times, answers "where do we stand?" in one place.

## Notes

CLI: Times#10 opens the note for a work item when a checkpoint with `#1234` is booked, through Context's CLI and never by writing Context's files. Add one command for that, for example `context item <number> --open`: it opens this work item's page in the running desktop app (starting it if needed) and creates the page when there is none. The spec decides how the CLI reaches the desktop app. Nothing else on the page needs a CLI command.
