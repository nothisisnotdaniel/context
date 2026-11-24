# 264 Check auto save by hand in the desktop app

Category: test

## What

Run the desktop app and check the parts job 203 could not test automatically: the 1 s pause save in the editor (view.js), the Saving…/Saved status in light and dark mode, and closing the window with unsaved text (it saves, then closes; a failed save asks Discard changes).

## Why

The spec of job 203 asks for this manual check; the editor script and window close path have no automated tests.

## Notes

Proposed by job 203-note-auto-save (`factory/jobs/203-note-auto-save/`).

## Manual verification

On 2026-11-24 the delegated Windows check at `29b1b32` observed `Saving…` and `Saved.` in native WebView2 in Light and Dark. A second edit restarted the pause, and small notes began saving 1.01–1.02 seconds after the last input. Closing a dirty note saved its text before the process exited. With a disposable note held against replacement, close showed the discard question. Cancel preserved the edit and kept the window open. Discard closed it and preserved the original file.
