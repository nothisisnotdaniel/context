# 126 Keep stale drafts out of the Windows delta base

As a Windows user, I want each update built against the last published release, so that a leftover draft never becomes the base of a delta package.

## What

The Windows Pack step runs `vpk download github --token`, which also sees draft releases (Velopack 1.2.161 has no draft filter), so a stale draft holding Windows assets can become the delta base. Delete stale drafts before the pack, or download the previous release without the token so only published releases are seen. Add tests.

## Why

Raised by the security review of job 135 (finding 3): the updater falls back to the full package, so it still works, but deltas can be built from unpublished files.

## Notes

Proposed by job 135-release-draft-publish (`factory/jobs/135-release-draft-publish/`).

Proposed by job 135-release-draft-publish (`factory/jobs/135-release-draft-publish/`).
