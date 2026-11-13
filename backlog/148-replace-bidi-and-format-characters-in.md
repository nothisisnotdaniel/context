# 148 Replace bidi and format characters in shown text

As a Context user, I want file names and note text shown without hidden direction-changing characters, so that a crafted name cannot make a message read as something else.

## What

Extend DesktopText.OneLine and SafeText.Clean to replace Unicode bidi and format characters (U+202A-U+202E, U+2066-U+2069, U+200E/F and similar) like they replace control characters, with tests.

## Why

Review-security (job 139): OneLine replaces C0/C1 controls but keeps bidi characters such as U+202E, so a crafted file name can reorder the shown text.

## Notes

Proposed by job 139-desktop-import-export (`factory/jobs/139-desktop-import-export/`).
