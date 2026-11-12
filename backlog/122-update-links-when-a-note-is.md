# 122 Update [[links]] when a note is renamed in the desktop app

As a Context user, I want renaming a note to update the [[Old name]] links that point at it, so that my links keep working after a rename.

## What

When a note is renamed in the desktop app, rewrite [[Old name]] links in other notes to [[New name]], or at least show how many links will break and ask first.

## Why

Raised by the code review of job 134: today a rename leaves [[Old name]] links pointing at a note that no longer exists, and nothing tells the user.

## Notes

Proposed by job 134-desktop-notes.

Proposed by job 134-desktop-notes (`factory/jobs/134-desktop-notes/`).
