# 426 Show the punycode host in a web link title

As a Context user, I want the link title to show lookalike hosts in their punycode form, so that I can spot a fake domain before opening it.

Category: security

## What

In the editor link title, show the host of an IDN URL in its xn-- form (or both forms) instead of only the Unicode form as written.

## Why

Raised by review-security in job 354: a homograph host such as one with a Cyrillic a opens as written and the title shows the Unicode form.

## Notes

Proposed by job 354-web-links-editor (`factory/jobs/354-web-links-editor/`).
