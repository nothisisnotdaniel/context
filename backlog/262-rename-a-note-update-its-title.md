# 262 Rename a note: update its # title line too

Category: feature

## What

In the desktop app, when a note is renamed and its first line is `# <old name>`, rewrite that line to `# <new name>` so it stays hidden in the editor (job 202 hides a first-line title only when it matches the note name). Leave notes whose first line is not the old title unchanged.

## Why

After job 202 a renamed note keeps its old `#` line, which then shows inside the editor as a level-2 heading; the UX review of job 202 noted this.

## Notes

Proposed by job 202-note-title-once (`factory/jobs/202-note-title-once/`).
