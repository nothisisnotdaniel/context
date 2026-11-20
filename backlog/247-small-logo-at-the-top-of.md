# 247 Small logo at the top of the sidebar

As a Context user, I want a small Context logo at the top left of the sidebar, so that the window has a recognisable identity.

## What

In the desktop app's sidebar header (NotesPanel.razor, where the "Notes" label sits today, next to the Triage, Tags and Settings icon buttons): show a small logo (about the height of those icon buttons) in place of the "Notes" label. Use the app's own icon if the repo has one, otherwise a simple inline SVG mark shipped with the app (no CDN, no new package). It keeps an accessible name ("Context") for screen readers, and the note list keeps an accessible name now that the visible "Notes" label is gone. Readable in light and dark mode.

## Why

The sidebar header only says "Notes"; a logo makes the app recognisable and frees the label's room.

## Notes

Requested by the user on 2026-11-16 after job 193 ("where it says notes today").
