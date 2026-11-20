# 380 Mail digest: hint under the Settings checkbox

As the user, I want a short note under the Mail digest checkbox in Settings, so that I know what turning it on reads before I do it.

## What

In SettingsPanel.razor, add one grey hint line under the Mail digest checkbox, for example: 'Reads the subject and the start of the body when you click Read mail; private mail is skipped.' Keep the wording in line with CLAUDE.md's Inbox rule and add a test that checks it.

## Why

UX review of job 286 (nit 1): the checkbox has no hint of its own.

## Notes

Proposed by job 286-mail-digest (review-ux 1).

Proposed by job 286-mail-digest (`factory/jobs/286-mail-digest/`).
