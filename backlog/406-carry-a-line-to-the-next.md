# 406 Carry a line to the next meeting with - [>]

As a Context user, I want to mark any line in a meeting note with - [>] so that it is carried into the next meeting's note without moving it under an Open points heading.

## What

A list line written as `- [>] text` (any indent, anywhere in the note, outside code fences) is copied into the next occurrence's `## From last time` section when that note is created, as `- text` (the marker removed), in line order together with the open `- [ ]` todos and the `Open points` lines; a line that is both in an `Open points` section and marked is copied once. `- [>]` is not a todo: it does not show in the todo list or the Todos page, and the editor shows it as plain text, not as a checkbox or a quote. The previous note stays unchanged. The README meeting-notes section and the slash-command help mention the marker.

## Why

Moving points under an `Open points` heading breaks up the structure of a note, and only checkboxes carry over today. `- [>]` is the bullet-journal "migrated" mark (also used by Obsidian Tasks), sits naturally next to `- [ ]` and `- [x]`, and does not clash with Markdown quotes the way `>>` would.

## Notes

Asked for on 2026-11-20. Builds on meeting-note carry-over (job 245, `MeetingCarryOver.Extract`). Check how the editor (`editor/src/markdown.js`, `schema.js`) parses `- [>]` today so it is not rewritten on save.
