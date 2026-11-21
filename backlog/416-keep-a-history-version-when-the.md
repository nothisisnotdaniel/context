# 416 Keep a history version when the app numbers decisions

As a Context user, I want the old text of a note kept as a history version when the app writes decision numbers into it, so that I can always get back what I typed.

Category: security

## What

When saving writes #!d numbers into a note (NumberWritten and the other write paths from job 343), store the previous bytes as a NoteHistory version first, as other app rewrites do.

## Why

Raised by the security review of job 343 round 2: numbering rewrites user text without a history version, so a wrong rewrite cannot be undone from history.

## Notes

Proposed by job 343-numbered-decisions.
