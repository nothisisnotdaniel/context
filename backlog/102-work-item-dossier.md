# 102 Work item dossier

As a developer, I want one page per work item with its notes, todos, hours, commits and pull requests, so that I can pick a work item back up in a minute.

## What

`context item <number>` (and a view in the desktop app once it has one) shows, for one work item: the notes and todos that mention it, the booked hours and checkpoint texts from `times report --json` when Times is installed, and the commits whose message mentions it in configured repository folders (git with a fixed argument list, never a shell). Each source is skipped with a short hint when it isn't available. Full path.

## Why

No single tool shows everything about a work item, so the picture is scattered over the board, the repo, the notes and the time sheet. Bringing it together is the suite's main selling point.
