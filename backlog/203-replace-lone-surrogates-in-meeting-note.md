# 203 Replace lone surrogates in meeting note names

As a Context user, I want a meeting note to be created even when the appointment title holds broken characters, so that /meeting never fails on a name the file system rejects.

## What

In MeetingNote (src/Context.Core/MeetingNote.cs, the name cleaning around line 229), replace every unpaired UTF-16 surrogate in the title with a space before the other clean-up steps, not only one at the cut point. Update the spec text for the name rules and add tests with a lone high and a lone low surrogate.

## Why

An unpaired surrogate from .ics data passes ValidateTitle, and APFS may reject it, so /meeting shows "Can't write <name>." for an appointment whose title only has one broken character.

## Notes

Proposed by job 167-meeting-notes-appointments (review-security round 1, nit 2). The CLI/desktop catch difference (review-code nit 2) is covered by #197.

Proposed by job 167-meeting-notes-appointments (`factory/jobs/167-meeting-notes-appointments/`).
