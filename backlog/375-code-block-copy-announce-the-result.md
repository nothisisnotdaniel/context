# 375 Code block Copy: announce the result to screen readers

As a screen reader user, I want to hear whether copying a code block worked, so that I know the snippet is on my clipboard.

## What

The code block Copy button (src/Context.Desktop/editor/src/view.js) shows Copied or Can't copy only as the button's textContent, which a screen reader may not announce. Add a polite aria-live region (or role=status) that gets the same text, and a test that checks it.

## Why

UX review of job 283 (nit 1).

## Notes

Proposed by job 283-code-blocks-inline (review-ux 1).

Proposed by job 283-code-blocks-inline (`factory/jobs/283-code-blocks-inline/`).
