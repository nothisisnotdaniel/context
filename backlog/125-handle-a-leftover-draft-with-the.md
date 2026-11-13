# 125 Handle a leftover draft with the same tag

As a maintainer, I want a release run to deal clearly with an existing draft of the same tag, so that a failed run never leaves two drafts with one tag name.

## What

When the release job creates its draft and a draft for $TAG already exists (for example after a failed release-PR run), either delete the stale draft first or fail with a clear message naming it. Decide which in the spec, and add tests.

## Why

Raised by the security review of job 135 (finding 2): after a failed run two drafts can share a tag name; it fails closed today, but needs manual clean-up.

## Notes

Proposed by job 135-release-draft-publish (`factory/jobs/135-release-draft-publish/`).

Proposed by job 135-release-draft-publish (`factory/jobs/135-release-draft-publish/`).
