# 053 Note history

As a Context user, I want earlier versions of a note kept and restorable, so that I can undo a bad edit or a lost paragraph.

## What

Keep a snapshot of a note before each save in `history/` (encrypted for sensitive notes, at most N per note and a size cap, oldest removed first). The desktop app shows a note's versions, the difference to the current text, and restores one after asking.

## Why

Without history, an accidental overwrite is permanent. Caps keep the history from filling the disk, and sensitive notes stay encrypted in it.
