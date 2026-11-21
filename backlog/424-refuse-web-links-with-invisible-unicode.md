# 424 Refuse web links with invisible Unicode format characters

As a Context user, I want links that hide invisible direction or zero-width characters to be refused, so that a link cannot look like one address and open another.

Category: security

## What

In editor/src/markdown.js webHref and src/Context.Desktop/WebLink.cs, also refuse URLs that contain Unicode format characters (category Cf, such as U+202E, U+200B, U+2066 to U+2069), not only whitespace and control characters.

## Why

Raised by review-security in job 354: Cf characters pass both checks today, so a link can show a misleading address.

## Notes

Proposed by job 354-web-links-editor (`factory/jobs/354-web-links-editor/`).
