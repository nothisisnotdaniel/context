# 066 Rename with link update

As a Context user, I want renaming a note to update every link to it, so that renaming never breaks links.

## What

Renaming a note in the desktop app also rewrites every `[[link]]` to it (keeping `#heading` and `|alias` parts, skipping code), after showing how many notes would change; each changed note gets a history snapshot first. Builds on desktop writes and note history.

## Why

Without it, renaming a note leaves dead links behind, which makes me avoid renaming at all. A history snapshot of each changed note makes it safe.
