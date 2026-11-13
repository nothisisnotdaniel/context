# 150 Require a .md source for desktop import

As a Context user, I want the desktop import to refuse files that are not Markdown, so that I don't store a binary or unrelated file as a note by mistake.

## What

Make DesktopActions.ImportNote refuse sources without a .md extension with a clear message; decide whether the console /import should do the same so both stay alike.

## Why

Review-security (job 139): ImportNote does not require a .md source; only the dialog filter does, so any file up to 5 MB can be stored as a note.

## Notes

Proposed by job 139-desktop-import-export (`factory/jobs/139-desktop-import-export/`).
