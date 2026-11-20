# 268 Remove the Save button from the note editor

## What

In the desktop app (src/Context.Desktop/NoteEditor.razor, CommandPalette.razor, app.css): remove the Save button next to the note editor. Auto save (job 203, #245) saves after the last edit and before the note is left or the window closes, so the button is no longer needed. Keep Ctrl/Cmd+S as a quiet 'save now' shortcut (the spec decides whether /save stays in the command palette). The save status line (saved, saving, failed) stays where the user can see that a save failed. Update the tests that click the Save button so they trigger the save another way. Light and dark mode.

## Why

Auto save takes care of saving, so the button only adds clutter and suggests that a note is lost unless it is clicked.

## Notes

Requested by the user on 2026-11-18 ("Remove the save button. Auto save should take care of it."). #251 left the Save button for the auto save item to decide.
