# 270 Editor works on a day opened from the calendar

## What

In the desktop app: when the user opens a day with the Pick a day calendar in the top bar (TopBar.razor, Main.razor), the note of that day cannot be edited. Find out why and fix it so the editor is always in edit mode: on a day picked from the calendar, on today, after Today, Home, the previous/next day and any other way of reaching a day or a note, typing edits the note at once and auto save saves it. There is no separate read-only view. Add a test that opens a day through the day picker and edits it.

## Why

The user expects to write in every note they open; a note that silently ignores typing after picking a day looks broken.

## Notes

Requested by the user on 2026-11-18 ("When I use the calendar to get to a day the editor is not working there. We have to always be in edit mode!").
