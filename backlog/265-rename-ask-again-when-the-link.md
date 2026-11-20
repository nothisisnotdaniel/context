# 265 Rename: ask again when the link count changed before Confirm

As a Context user, I want the rename to stop and ask again when the number of notes whose links would change is different from what I confirmed, so that a rename never rewrites notes I did not agree to.

## What

In the desktop app, RenameNoteNow (src/Context.Desktop/DesktopActions.cs) rebuilds the link plan after the user confirms. If the rebuilt plan changes a different number of notes than the count shown in the rename question, do not rename: show the question again with the new count (new wording in DesktopText). Add tests for a note gaining or losing a link between the question and Confirm.

## Why

Raised by the security review of job 206: notes can change between the question and Confirm (auto save, another window), so the count the user agreed to can differ from what is rewritten. Each change has a history snapshot, so nothing is lost, but the user was not asked about it.

## Notes

Proposed by job 206-rename-link-update (`factory/jobs/206-rename-link-update/`).
