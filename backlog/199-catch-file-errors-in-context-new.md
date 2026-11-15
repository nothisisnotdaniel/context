# 199 Catch file errors in context new --template

As a Context user, I want `context new --template` to report a file error with its fixed message, so that I never see a raw exception from the command line.

## What

In Cli.cs NewNoteFromTemplate, move the NoteTemplates.Prepare call (which lists notes to check for an existing one) inside the try block, so an IOException or UnauthorizedAccessException gets the same fixed error line and exit code as the desktop path. Add a test.

## Why

Today an unexpected IO error while listing notes escapes as an unhandled exception on the CLI, while the desktop app shows a fixed message.

## Notes

Proposed by job 165-note-templates (review-code round 1, nit 1).

Proposed by job 165-note-templates (`factory/jobs/165-note-templates/`).
