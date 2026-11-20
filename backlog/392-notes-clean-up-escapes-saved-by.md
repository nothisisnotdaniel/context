# 392 Notes: clean up escapes saved by the old editor

As the user, I want notes the old editor saved with backslash escapes or HTML entities to read cleanly, so that I don't see stray \* or &amp; in my notes.

Category: bug

## What

Since job 290 the editor never escapes on save, so notes written earlier with escapes like \* or \` or entities like &amp; now show them raw in the editor. Offer a one-time, reviewed cleanup (for example a context command or a per-note action) that removes escapes the old editor added where they are not needed, with tests and no change to notes the user wrote by hand.

## Why

UI and UX reviews of job 290 (nit): accepted by the spec and documented, but older notes now look noisy.

## Notes

Proposed by job 290-backtick-code-input (review-ui 1, review-ux 2).

Proposed by job 290-backtick-code-input (`factory/jobs/290-backtick-code-input/`).
