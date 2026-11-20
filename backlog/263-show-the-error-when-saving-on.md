# 263 Show the error when saving on window close fails

Category: bug

## What

In the desktop app (CloseGuard.cs SaveThenCloseAsync), when the save that runs before closing the window throws, show the save error in the note editor status line instead of swallowing it; the window stays open as today.

## Why

Job 203 security review: the user gets no feedback on why the window did not close.

## Notes

Proposed by job 203-note-auto-save (`factory/jobs/203-note-auto-save/`).
