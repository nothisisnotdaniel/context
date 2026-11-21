# 429 Keep reference-style links byte for byte on save

As a Context user, I want reference-style links like [a][ref] kept as I typed them, so that saving does not rewrite my notes.

Category: bug

## What

In the editor markdown parser and serializer, round-trip reference-style links and their definitions unchanged, as job 354 did for inline links.

## Why

Noted by the spec writer of job 354: reference-style links stay as lossy as before that job.

## Notes

Proposed by job 354-web-links-editor (`factory/jobs/354-web-links-editor/`).
