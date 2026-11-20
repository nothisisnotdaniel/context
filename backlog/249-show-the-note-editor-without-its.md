# 249 Show the note editor without its own box

As a Context user, I want the note editor to have no box of its own, so that an open note looks the same as a note I am only reading.

## What

In the desktop app (src/Context.Desktop, editor.css and app.css; CSS only, no change to NoteEditor.razor logic or editor.js): remove the editor area's own border, rounded corners, background and inner inset, so that the text lines up with the page title above it and looks the same as a note shown without editing. Only the caret shows that it can be edited. Focus must still be visible for keyboard users (for example a subtle focus indicator that is not a box). Light and dark mode.

## Why

The editor screenshot shows the note text in a rounded box, which looks like a form instead of a note.

## Notes

Split out of #244 on 2026-11-16 so it can run in cheap mode; #244 keeps the title and heading-level parts.
