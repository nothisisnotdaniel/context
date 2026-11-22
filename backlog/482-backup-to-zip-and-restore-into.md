# 482 Backup to ZIP and restore into a separate folder

As someone whose notes matter, I want to back up the whole data folder into one ZIP and restore a backup into a new folder, so that I can get my notes back without risking the live folder.

Category: feature

## What

Settings get `Back up now`: a ZIP of the data folder (notes, subpages, attachments, `.history`, todos, archive, done log, settings; not `context.db`, which is rebuilt; sealed notes stay sealed) written to a folder the user picks outside the data folder, named with the date and time, never overwriting. `Restore from backup` unpacks a chosen ZIP into a new, empty folder the user picks (never the live folder), refusing entries with absolute paths, `..`, links or over a size cap, and then says how to switch to it. A last-backup time shows in Settings.

## Why

Context has nothing built in for backups. assistant makes ZIP backups and restores into a separate folder, which is safe.

## Notes

Idea from Creacility/assistant PLAN.md (compared on 2026-11-22).
