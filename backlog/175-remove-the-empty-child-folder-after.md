# 175 Remove the empty child folder after a subpage move

As a Context user, I want an empty subpage folder removed when its last subpage moves away, so that my notes folder doesn't fill up with empty folders.

## What

After NoteStore.Move moves a subtree, remove the old parent's child folder when it is now empty (only when it has no other files).

## Why

Raised by the code review of job 152: the empty folder is harmless because List() ignores it, but it clutters the user's files and sync tools.

## Notes

Proposed by job 152-pages-hierarchy (`factory/jobs/152-pages-hierarchy/`).
