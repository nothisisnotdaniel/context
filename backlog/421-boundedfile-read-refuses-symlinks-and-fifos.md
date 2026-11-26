# 421 BoundedFile.Read refuses symlinks

As a Context user, I want the app to refuse a symlink where it expects a note or decision-folder.json, so that a planted special file cannot make it read elsewhere or hang.

Category: security

## What

Change the shared BoundedFile.Read so it does not follow a symlink, with a clear error. It is used for notes and for decision-folder.json; add a test for a symlink. The non-regular-file half (FIFO, device) is done: job 275-bounded-regular-file-reads (commit 3b7746b) refuses a file that is not seekable.

## Why

Raised by review-security in job 349: decision-folder.json is read through BoundedFile.Read, which follows a symlink and opens with FileMode.Open before the CanSeek check, as every note read does.

## Notes

Proposed by job 349-decision-folder-prefix (`factory/jobs/349-decision-folder-prefix/`).

## Folded in from #362 (backlog audit, 2026-11-26)

Notes: refuse a note swapped for a symlink between listing and reading: NoteStore.Files drops symlinked *.md entries at listing time, but BoundedFile.Read (src/Context.Core/BoundedFile.cs) then opens the path and follows links, so a regular x.md swapped for a link between List() and Read() is still read (TOCTOU). Check the path right before opening (FileInfo.LinkTarget and attributes) or open with no-follow where the OS supports it, and refuse with the existing unreadable-note message; add a test that swaps a listed note for a link.

## Folded in from #363 (backlog audit, 2026-11-26)

Calendar: skip symlinked .ics files: CalendarStore (src/Context.Core/CalendarStore.cs) now reads calendar/*.ics through BoundedFile, size-capped and FIFO-safe, but a symlinked .ics pointing outside the root is still read. Apply the same IsLink filter NoteStore.Files uses when listing calendar files, and add a test with a linked .ics that is skipped.

Reworded in the backlog audit, 2026-11-26.
