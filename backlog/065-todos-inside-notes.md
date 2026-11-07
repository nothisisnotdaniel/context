# 065 Todos inside notes

As a Context user, I want `- [ ]` lines in notes to show in the todo sidebar, so that action items from meeting notes don't get lost.

## What

A `- [ ]` line in a note (outside code, like tags) shows in the todo sidebar with a link to its note, and a due date written as `due:yyyy-MM-dd` on that line sorts it; ticking it in the sidebar writes `- [x]` back into that line of the note (only if the line is still unchanged, otherwise a hint), and ticking it in the note removes it from the sidebar. Stored in the index (a parser version bump). Works with encryption on. Builds on desktop writes.

## Why

Action items are usually written in the middle of a note, where they're forgotten. Showing them in the sidebar and syncing the tick both ways (only when the line is unchanged) keeps one source.
