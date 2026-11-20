# 381 Mail digest: keep mail text inert in meeting notes

As the user, I want a meeting note made from a mail to keep the mail's subject and sender as plain text, so that a mail can't add tags, links or mentions to my notes.

Category: security

## What

CreateMailNoteNow (src/Context.Desktop/DesktopActions.cs) and MailDigest (src/Context.Core/MailDigest.cs) write the subject (as title) and sender name verbatim apart from control characters, so '#tag', '[[link]]' or '@name' in a mail become live in the note. Escape or neutralise these in the note body (and decide how the title handles them), with tests for each.

## Why

Security review of job 286 (nit 1): mail is outside text, and it now reaches notes after one click.

## Notes

Proposed by job 286-mail-digest (review-security 1).

Proposed by job 286-mail-digest (`factory/jobs/286-mail-digest/`).
