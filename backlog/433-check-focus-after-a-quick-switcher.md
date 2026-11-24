# 433 Check focus after a quick switcher pick

As a Context user, I want the cursor to land in the editor after I pick a note in the quick switcher, so that I can type at once.

Category: bug

## What

Check by hand whether the focus request on .layout after a pick races with the editor taking focus. If it does, skip the layout focus when a note opens, and update Enter_OpensTheNote.

## Why

Raised by the code review of job 364: after a pick that opens a note, the layout focus request may race with the editor taking focus.

## Notes

Proposed by job 364-quick-switcher (`factory/jobs/364-quick-switcher/`).

## Manual verification

Windows WebView2 at `29b1b32`, 2026-11-24: all eight switches to another note accepted immediate typing. All four reselections of the open note, using Enter or the pointer, left focus on `.layout` and the test files unchanged. An editor click restored typing. Repeat the check after [#504](504-focus-the-editor-when-the-quick.md).
