# 382 Mail digest: strip Unicode format characters from suggestions

As the user, I want bidi overrides and zero-width characters removed from mail suggestions, so that a todo or note title from a mail always reads the way it is stored.

## What

MailDigest.CleanText and TitleFor (src/Context.Core/MailDigest.cs) drop only char.IsControl characters. Also remove Unicode format characters (category Cf, such as U+202A-202E, U+2066-2069, U+200B-200F), and add tests with a bidi-override subject.

## Why

Security review of job 286 (nit 2): a mail could make a suggestion look different from what is saved.

## Notes

Proposed by job 286-mail-digest (review-security 2).

Proposed by job 286-mail-digest (`factory/jobs/286-mail-digest/`).
