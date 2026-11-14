# 176 Merge Find and FindExact in NoteStore

As a Context developer, I want one note-name matching function, so that exact and ignore-case lookup can't drift apart.

## What

In src/Context.Core/NoteStore.cs, Find(IReadOnlyList<NoteInfo>, ...) and FindExact(List<Entry>, ...) do the same exact-then-ignoring-case match. Share one implementation, keeping the sensitive-note lookup working.

## Why

Raised by the code review of job 152: two copies of the same matching rule will diverge the next time one is changed.

## Notes

Proposed by job 152-pages-hierarchy (`factory/jobs/152-pages-hierarchy/`).
