# 107 Own-cloud sync spike

As the developer of the suite, I want to know how Context and Times can sync through the user's own OneDrive or iCloud folder, so that the apps work on two machines without a server.

## What

Find out what breaks when the data folders sit in OneDrive or iCloud Drive on macOS and Windows: SQLite (`context.db`) in a synced folder, files that are only placeholders until opened, conflict copies, and the same file edited on two machines. Try it on this machine where possible. Write findings and one suggested backlog item.

## Why

Syncing through the user's own cloud keeps the suite free to run, but synced folders are known to break databases and create silent conflicts. Finding out first avoids data loss.
