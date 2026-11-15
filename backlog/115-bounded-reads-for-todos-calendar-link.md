# 115 Bounded reads for todos, calendar link and notes

As the user, I want Context to refuse oversized or non-regular data files, so that a stray huge file or a device node cannot hang or crash the app.

## What

TodoStore (todos.json), CalendarLink (calendar-link.txt) and NoteStore.Read use File.ReadAllBytes/ReadAllText with no size limit and no regular-file check. Add a size cap and a regular-file check, as other stores already do, with a clear error, and tests.

## Why

Raised by the security review of job 132: unbounded reads can exhaust memory or block on special files.

## Notes

Proposed by job 132-remove-folder-encryption (`factory/jobs/132-remove-folder-encryption/`).

Merged on 2026-11-15: #139 (calendar-link.txt through BoundedFile with a small limit, since a link is at most 2048 characters, and a fixed error text), #166 (todos.json must not block desktop start-up or a CLI command) and #204 (on failure, report the same fixed error as other unreadable todo files and change nothing).
