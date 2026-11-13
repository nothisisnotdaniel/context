# 149 Clear earlier import lines on a cancelled import

As a Context user, I want the import result lines to clear when I cancel a new import, so that old results are not mistaken for the latest.

## What

In NotesPanel.razor, clear the previous import's result lines when the file picker is cancelled, and adjust the cancelled-import tests.

## Why

Review-ui and review-ux (job 139): a cancelled import leaves the lines of an earlier import on screen until the next import.

## Notes

Proposed by job 139-desktop-import-export (`factory/jobs/139-desktop-import-export/`).
