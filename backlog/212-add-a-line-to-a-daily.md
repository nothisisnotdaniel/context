# 212 Add a line to a daily note from the CLI

As a Times user, I want Times' end-of-day ritual to save my plan for tomorrow into Context, so that the plan is waiting in tomorrow's daily note.

Category: feature

## What

Add `context today --add <text...>` (or a `--date yyyy-MM-dd` for another day, for example tomorrow): it appends the text as one line to that day's daily note, creating the note from the daily-note rules when it is missing, through ReadForEdit and SaveIfUnchanged like `capture`. Same text rules as `capture` (controls to spaces, 1-1000 chars). A sensitive note is `Cli.SensitiveText`, a failed write `Can't write <name>.`, exit 1. Prints `Added to <name>.`.

## Why

Times#38 asks for one line of plan for tomorrow and saves it as a Context note, without writing Context's files. No command can add text to a note today; `capture` only writes to the inbox.

## Notes

Needed by Times#38 (End-of-day ritual). From the CLI review on 2026-11-15.
