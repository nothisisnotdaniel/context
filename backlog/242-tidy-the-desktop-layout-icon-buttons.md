# 242 Tidy the desktop layout: icon buttons, no dates, set-apart capture bar

As a Context user, I want a calmer, more compact desktop window, so that the sidebar has room for note names and the capture bar is easy to tell apart from the note I am working on.

## What

Three changes in the desktop app (src/Context.Desktop): (1) The Triage, Tags and Settings buttons at the top of the sidebar (NotesPanel.razor, .notes-bar-buttons) become icon-only buttons: inline SVG icons shipped with the app (no CDN, no new package), each keeping its accessible name (aria-label) and a tooltip (title) with the old text, readable in light and dark mode. (2) The sidebar note list no longer shows each note's modified date and time (span.time in NotesPanel.razor), so a row is the toggle, the name and the locked mark only. (3) The quick capture bar at the top of the main area (CaptureBox.razor, .capture-box, placeholder 'Capture to inbox') is visually set apart from the content below it, for example with its own background band and a bottom border, in light and dark mode, without changing how capture works.

## Why

The text buttons take most of the sidebar header and the dates take room from the note names, so long names are cut off early. The capture bar currently looks like part of the open note, which makes the main window feel cluttered and makes it unclear where the note starts.

## Notes

Requested by the user on 2026-11-16.
