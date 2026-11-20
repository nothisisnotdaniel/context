# 018 Write todo ids once

As a Context user, I want the derived todo ids written into `todos.json` once, so that ids stay stable instead of being derived again on every read.

## What

Write the derived ids into `todos.json` once during migration (one write, on first unlock or start), instead of deriving them on every read until the next write. Keep an older build that rewrites the file from dropping ids in mind.

## Why

Deriving ids on every read until the next write is fragile, and an older build that rewrites the file can drop them. One write during migration settles it.
