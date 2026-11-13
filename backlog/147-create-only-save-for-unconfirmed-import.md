# 147 Create-only save for unconfirmed import

As a Context user, I want an import that I did not confirm as a replace to never overwrite a note, so that a note created in the same moment by another process is not lost.

## What

Add a create-only mode to Core's NoteStore.Save (fail if the file exists) and use it for unconfirmed imports in DesktopActions.ImportNote, instead of TryFind followed by Save.

## Why

Review-security (job 139): an unconfirmed import checks TryFind and then saves, so a note created in between is replaced without asking.

## Notes

Proposed by job 139-desktop-import-export (`factory/jobs/139-desktop-import-export/`).
