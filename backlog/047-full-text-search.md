# 047 Full-text search

As a Context user, I want to search the content of my notes, so that I can find a note even when I don't remember its title or tags.

## What

Desktop app, plus a CLI command where it fits. A `/search <text>` command lists the notes whose content contains the text, with the matching line, using SQLite FTS5 in the existing database and updated by `Sync` like the tag index. Sensitive notes are never indexed.

## Why

Tags only help when I used them. SQLite FTS5 is built into the database Context already has; sensitive notes are never indexed.
