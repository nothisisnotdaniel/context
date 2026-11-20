# 391 Editor: type an inline code span that holds a backtick

As the user, I want to type inline code that contains a backtick, so that I can write any code snippet from the keyboard.

## What

The inline code input rule in src/Context.Desktop/editor/src/view.js matches only `x` spans without a backtick inside, so a span like ``a`b`` cannot be typed. Support the double-backtick form (``…``) in the input rule and the serializer, with round-trip tests.

## Why

UX review of job 290 (nit 1): within spec, but a gap in keyboard-only code entry.

## Notes

Proposed by job 290-backtick-code-input (review-ux 1).

Proposed by job 290-backtick-code-input (`factory/jobs/290-backtick-code-input/`).
