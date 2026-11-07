# 022 Concurrent todo writes

As a Context user, I want two programs writing my todos at once to never lose an update, so that the desktop app and the CLI can be used side by side.

## What

Two processes writing `todos.json` at the same moment can lose one update (whole-file read-modify-write in Core). Add a file lock or a compare-on-write retry.

## Why

Core reads, changes and writes the whole `todos.json`, so two writers at the same moment can overwrite each other. A file lock or compare-on-write retry fixes it.
