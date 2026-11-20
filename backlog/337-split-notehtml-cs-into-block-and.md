# 337 Split NoteHtml.cs into block and inline renderers

As a developer, I want the note-to-HTML renderer split into smaller files, so that it is easier to read and change safely.

## What

src/Context.Core/NoteHtml.cs is 835 lines. Split it into a block renderer (headings, lists, tables, code blocks, quotes) and an inline renderer (links, emphasis, code spans, tags), with no change in output; the existing NoteHtmlTests must pass unchanged.

## Why

Code review of job 250: one large file mixes two levels of parsing, which makes later changes riskier.

## Notes

Proposed by job 250-share-a-note (review-code 1 round 1, review-code 2 round 2).

Proposed by job 250-share-a-note (`factory/jobs/250-share-a-note/`).
