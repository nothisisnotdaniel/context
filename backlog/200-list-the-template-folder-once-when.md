# 200 List the template folder once when a template is missing

As a Context developer, I want a missing template to list the templates folder only once, so that the code path is simpler and does no double work.

## What

In NoteTemplates.TryRead / Prepare, reuse the entries already listed for the missing-template hint instead of enumerating the templates folder a second time.

## Why

The missing case enumerates the folder twice. It is harmless but wasteful and makes the code harder to follow.

## Notes

Proposed by job 165-note-templates (review-code round 1, nit 2).

Proposed by job 165-note-templates (`factory/jobs/165-note-templates/`).
