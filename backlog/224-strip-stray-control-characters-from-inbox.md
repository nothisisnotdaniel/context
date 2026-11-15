# 224 Strip stray control characters from inbox lines before triage writes

As a Context user, I want an inbox line that holds a stray control character to land cleanly in the note or todo I triage it into, so that hidden characters don't end up in my stored text.

## What

In InboxTriage (Lines, or before each target write), remove or refuse a lone `\r` and other control characters in a line's text before it is written to a new note, a todo or an appended line. Decide which, keep the inbox file itself unchanged until the line is triaged, and add tests.

## Why

Only `\r\n` is folded today, so a hand-edited inbox line with a lone `\r` is copied verbatim into the target.

## Notes

Proposed by job 179-inbox-triage (review-security round 1, nit 1).

Proposed by job 179-inbox-triage (`factory/jobs/179-inbox-triage/`).
