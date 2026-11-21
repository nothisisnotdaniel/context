# 428 Keep unrelated status messages when a web link opens

As a Context user, I want opening a link to clear only its own earlier error, so that other status messages are not lost.

Category: ui

## What

In Main.razor OpenWebLink, clear the status line on a successful open only when it shows an earlier link refusal, and leave any other status message.

## Why

Raised by review-ui in job 354: a successful open clears the status line and also wipes unrelated status messages.

## Notes

Proposed by job 354-web-links-editor (`factory/jobs/354-web-links-editor/`).
