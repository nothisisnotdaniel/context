# 362 Notes: refuse a note swapped for a symlink between listing and reading

As the user, I want Context to refuse a note that became a symlink after it was listed, so that a note read can never follow a link to a file outside my data folder.

## What

NoteStore.Files drops symlinked *.md entries at listing time, but BoundedFile.Read (src/Context.Core/BoundedFile.cs) then opens the path and follows links, so a regular x.md swapped for a link between List() and Read() is still read (TOCTOU). Check the path right before opening (FileInfo.LinkTarget and attributes) or open with no-follow where the OS supports it, and refuse with the existing unreadable-note message; add a test that swaps a listed note for a link.

## Why

Security review of job 275 (nit 1): low risk, since the attacker must already be able to write the notes, but it leaves a gap in the symlink rule job 275 added.

## Notes

Proposed by job 275-bounded-regular-file-reads (review-security 1).

Proposed by job 275-bounded-regular-file-reads (`factory/jobs/275-bounded-regular-file-reads/`).
