# 053 Note history

As a Context user, I want earlier versions of a note kept and restorable, so that I can undo a bad edit or a lost paragraph.

## What

Keep a snapshot of a note before each save in a `.history/` folder next to the note, inside the note's own hierarchy level (for example `Work/Projects/.history/` for a note in `Work/Projects/`), so the history moves and is removed together with its level (encrypted for sensitive notes, at most N per note and a size cap, oldest removed first). The desktop app shows a note's versions, the difference to the current text, and restores one after asking.

Note hierarchies (folders and levels) may not start with a `.`: creating or renaming one to a name like `.history` or `.drafts` is refused with a clear message, and dot-folders are never listed or indexed as notes.

## Why

Without history, an accidental overwrite is permanent. Caps keep the history from filling the disk, and sensitive notes stay encrypted in it. Reserving dot-names keeps `.history/` (and any later internal folder) from clashing with a user's own notes.
