# 195 Editor: don't show a link that runs into a code span

As a Context user, I want the editor to show [[...]] as a link only when the index treats it as one, so that what I see while typing matches what links and backlinks show.

## What

Make the editor's noteLinkRule in src/Context.Desktop/editor/src/markdown.js skip a [[...]] whose text runs into an inline code span (for example [[a `b]]), the same way NoteLinkParser does since job 163. Extend EditorBundleTests with that case.

## Why

Since job 163 the index no longer counts [[a `b]] as a link, but the editor still highlights it as one, so a note looks linked when it isn't.

## Notes

Proposed by job 163-tag-link-parser-fixes (review-code round 1, nit 2).

Proposed by job 163-tag-link-parser-fixes (`factory/jobs/163-tag-link-parser-fixes/`).
