# 366 Pinned list: roll back the in-memory change when the file write fails

As the user, I want the sidebar to show my pins as they are on disk, so that a pin or unpin that failed to save does not look saved until the next restart.

## What

DesktopActions.ChangePinned (src/Context.Desktop/DesktopActions.cs) changes the in-memory pinned list before writing pinned.json; on a failed write after a rename or delete the list stays changed. Keep the old list until the write succeeds (or restore it on failure), do the same for the recent list, and add tests with a write that fails.

## Why

Code review of job 278 (nit 1): documented and consistent with the recent list, but the sidebar can then show a state that is not saved.

## Notes

Proposed by job 278-recent-pinned-notes (review-code 1).

Proposed by job 278-recent-pinned-notes (`factory/jobs/278-recent-pinned-notes/`).
