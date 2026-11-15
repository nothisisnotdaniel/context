# 202 Match note and sensitive-file extensions ignoring case on Linux

As a Context user on Linux, I want notes named *.MD and sensitive files named *.CTXS to be found the same as on macOS, so that the same folder behaves the same on every OS.

## What

In NoteStore.AddLevel (*.md) and NoteStore.ListSensitive (*.ctxs), enumerate with EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive } (as NoteTemplates does since job 166), keeping the other options as they are. Add tests that create upper-case extensions and would fail on a case-sensitive file system.

## Why

EnumerateFiles with a pattern is case-sensitive on Linux, so a note or sensitive file with an upper-case extension is silently left out there while macOS lists it. Job 166 fixed the same problem for templates after it broke Linux CI.

## Notes

Proposed by job 166-template-list-linux-ci (spec-writer note).

Proposed by job 166-template-list-linux-ci (`factory/jobs/166-template-list-linux-ci/`).
