# 421 BoundedFile.Read refuses symlinks and FIFOs

As a Context user, I want the app to refuse a symlink or FIFO where it expects a note or decision-folder.json, so that a planted special file cannot make it read elsewhere or hang.

Category: security

## What

Change the shared BoundedFile.Read so it does not follow a symlink and refuses non-regular files (FIFO, device) before opening, with a clear error. It is used for notes and for decision-folder.json; add tests for a symlink and a FIFO.

## Why

Raised by review-security in job 349: decision-folder.json is read through BoundedFile.Read, which follows a symlink and opens with FileMode.Open before the CanSeek check, as every note read does.

## Notes

Proposed by job 349-decision-folder-prefix (`factory/jobs/349-decision-folder-prefix/`).
