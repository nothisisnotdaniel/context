# 425 Drop attach and other risky query parts from mailto links

As a Context user, I want a mailto link from a note to open a plain new mail, so that a note cannot make my mail app attach a local file.

Category: security

## What

In WebLink.cs, refuse or strip the attach query parameter (and decide on body and others) of mailto URLs before they reach the launcher.

## Why

Raised by review-security in job 354: mailto keeps its query, and some mail clients have honoured attach=/path.

## Notes

Proposed by job 354-web-links-editor (`factory/jobs/354-web-links-editor/`).
