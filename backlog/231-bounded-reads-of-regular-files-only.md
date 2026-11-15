# 231 Bounded reads of regular files only

As a Context user, I want every file Context reads to be a regular file of a sensible size, so that a special file, a symlink or a huge file can never hang or crash the app.

## What

- Regular files only (was #32): A pipe (FIFO) or other special file still hangs `/current` (a `*.ics` in `calendar/`), because it opens files with `FileMode.Open`. Read them through `BoundedFile` (as note editing and `/import` already do) or skip anything that isn't a regular file.
- Bounded reads for todos, calendar link and notes (was #115): TodoStore (todos.json), CalendarLink (calendar-link.txt) and NoteStore.Read use File.ReadAllBytes/ReadAllText with no size limit and no regular-file check. Add a size cap and a regular-file check, as other stores already do, with a clear error, and tests.
- Skip symlinked or non-regular note files (was #173): In NoteStore listing (AddLevel and the top level), skip *.md entries that are symlinks or not regular files, the same way linked folders are already skipped. Cover top-level and child notes with tests.
- Cap the size of note reads (was #188): Add one shared note size limit and use it wherever notes are read in full: NoteStore.Read callers, TagIndex.Sync and PageTypes.Load (types/* pages). A file over the limit is skipped with a clear message instead of being read into memory. Add tests for a note just under and just over the limit.
- Match note and sensitive-file extensions ignoring case on Linux (was #202): In NoteStore.AddLevel (*.md) and NoteStore.ListSensitive (*.ctxs), enumerate with EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive } (as NoteTemplates does since job 166), keeping the other options as they are. Add tests that create upper-case extensions and would fail on a case-sensitive file system.

## Why

- #32: A FIFO named `*.ics` blocks `/current` forever, because it opens files with `FileMode.Open`. Note editing and import already avoid this through `BoundedFile`.
- #115: Raised by the security review of job 132: unbounded reads can exhaust memory or block on special files.
- #173: Raised by the security review of job 152: a child file like Meetings/x.md pointing at ~/.ssh/id_rsa is still listed and read, while symlinked folders are skipped.
- #188: Today every note and types/* page is read with File.ReadAllText and no cap, so a very large file in the data folder can make indexing or type loading slow or run out of memory.
- #202: EnumerateFiles with a pattern is case-sensitive on Linux, so a note or sensitive file with an upper-case extension is silently left out there while macOS lists it. Job 166 fixed the same problem for templates after it broke Linux CI.

## Notes

- Merges #32, #115, #173, #188, #202 on 2026-11-16, to save the fixed cost of one job per item.
- Use quality mode, full path, high risk (file access). Use one shared helper (BoundedFile) and one note size limit everywhere.
- #115: Proposed by job 132-remove-folder-encryption (`factory/jobs/132-remove-folder-encryption/`). Merged on 2026-11-15: #139 (calendar-link.txt through BoundedFile with a small limit, since a link is at most 2048 characters, and a fixed error text), #166 (todos.json must not block desktop start-up or a CLI command) and #204 (on failure, report the same fixed error as other unreadable todo files and change nothing).
- #173: Proposed by job 152-pages-hierarchy (`factory/jobs/152-pages-hierarchy/`).
- #188: Proposed by job 157-page-types-attributes (review-security round 1, nit 1).
- #202: Proposed by job 166-template-list-linux-ci (spec-writer note). Proposed by job 166-template-list-linux-ci (`factory/jobs/166-template-list-linux-ci/`).
