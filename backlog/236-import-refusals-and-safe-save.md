# 236 Import refusals and safe save

As a Context user, I want import to refuse odd files, never overwrite a note created meanwhile, and clear old result lines, so that import does only what I asked.

Category: security

## What

- Refuse odd files on import (was #99): Refusing zero-length or non-regular files on import changes /import behaviour
- Create-only save for unconfirmed import (was #147): Add a create-only mode to Core's NoteStore.Save (fail if the file exists) and use it for unconfirmed imports in DesktopActions.ImportNote, instead of TryFind followed by Save.
- Clear earlier import lines on a cancelled import (was #149): In NotesPanel.razor, clear the previous import's result lines when the file picker is cancelled, and adjust the cancelled-import tests.
- Require a .md source for desktop import (was #150): Make DesktopActions.ImportNote refuse sources without a .md extension with a clear message; decide whether the console /import should do the same so both stay alike.

## Why

- #99: Refusing them changes how `/import` behaves today, so job 123 left it for my approval.
- #147: Review-security (job 139): an unconfirmed import checks TryFind and then saves, so a note created in between is replaced without asking.
- #149: Review-ui and review-ux (job 139): a cancelled import leaves the lines of an earlier import on screen until the next import.
- #150: Review-security (job 139): ImportNote does not require a .md source; only the dialog filter does, so any file up to 5 MB can be stored as a note.

## Notes

- Merges #99, #147, #149, #150 on 2026-11-16, to save the fixed cost of one job per item.
- Cheap mode is enough, but it changes /import behaviour, so take the full path.
- #99: Proposed by job 123-scriptable-cli (`factory/jobs/123-scriptable-cli/`).
- #147: Proposed by job 139-desktop-import-export (`factory/jobs/139-desktop-import-export/`).
- #149: Proposed by job 139-desktop-import-export (`factory/jobs/139-desktop-import-export/`).
- #150: Proposed by job 139-desktop-import-export (`factory/jobs/139-desktop-import-export/`).
