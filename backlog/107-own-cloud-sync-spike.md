# 107 Own-cloud sync spike

As the developer of the suite, I want to know how Context and Times can sync through the user's own OneDrive or iCloud folder, so that the apps work on two machines without a server.

Category: feature

## What

Find out what breaks when the data folders sit in OneDrive or iCloud Drive on macOS and Windows: SQLite (`context.db`) in a synced folder, files that are only placeholders until opened, conflict copies, and the same file edited on two machines. Try it on this machine where possible. Write findings and one suggested backlog item.

## Why

Syncing through the user's own cloud keeps the suite free to run, but synced folders are known to break databases and create silent conflicts. Finding out first avoids data loss.

## Notes

- Candidate design from Creacility/assistant PLAN.md (compared on 2026-11-22): every saved version is a new file that never changes, each with a checksum; a conflict (two machines changed the same note) asks the user which version to keep instead of writing a conflict copy; settings that belong to one machine (window, paths, key store) stay per device and are never synced. Check this design against OneDrive and iCloud in the spike.
