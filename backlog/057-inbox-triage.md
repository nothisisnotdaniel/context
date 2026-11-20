# 057 Inbox triage

As a Context user, I want to walk through my inbox one line at a time and turn each into a note, a todo or nothing, so that the inbox gets emptied instead of growing.

## What

A desktop view that walks through `inbox.md` one line at a time and turns each into a new note, a todo (with an optional due date) or an addition to an existing note, drops it, or snoozes it ("later": tomorrow, next week or a typed date, kept on the line as `snooze:yyyy-MM-dd` so the file stays plain Markdown; it comes back to the top on that day), with keyboard shortcuts for each. A handled line is removed from the inbox only after its target is saved. Works with encryption on. Builds on quick capture.

## Why

Quick capture fills the inbox fast, but sorting it is tedious without a dedicated flow. Keyboard shortcuts and snoozing keep it quick and the file stays plain Markdown.
