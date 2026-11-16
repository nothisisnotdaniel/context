# 245 Auto save in the note editor

As a Context user, I want my note saved automatically when I stop typing, so that I never lose text and never have to think about saving.

## What

In the desktop app's note editor (NoteEditor.razor, EditorInterop, editor.js): when the user stops typing for a short pause (about 1 second; the spec picks the exact value), the note is saved through the same path as the Save button today (same checks, same conflict handling when the file changed on disk, same handling for sensitive notes). Edits made while a save runs are saved by the next one, and saves never overlap. Closing the note, opening another note or a page, and closing the window still save pending changes first, so the unsaved-changes prompt only appears when a save failed. Show a quiet status (for example "Saving…", "Saved" or the save error) instead of relying on the Save button; the spec decides whether the Save button stays. Cmd/Ctrl+S keeps saving at once.

## Why

Today the user must press Save; forgetting it means the unsaved-changes prompt or lost text.

## Notes

Requested by the user on 2026-11-16 after job 193. A failed auto save must stay visible and must not drop the text in the editor.
