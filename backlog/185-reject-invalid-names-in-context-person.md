# 185 Reject invalid names in context person

As a Context user, I want `context person` to tell me when a name can never be a person, so that I don't think the person simply has no notes.

## What

Have PersonView.Build (the `context person <name>` command) normalize the name with PersonParser.NormalizeTyped, as DesktopActions.OpenPerson does, and print a usage error with the usage exit code when the name is invalid (for example a/b).

## Why

Today `context person a/b` prints "No notes or appointments mention a/b.", which hides that the name is invalid and is inconsistent with the desktop app.

## Notes

Proposed by job 156-people-pages (review-security round 1, nit 2).
