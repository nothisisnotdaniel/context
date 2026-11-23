# 497 Show feedback while the appointment page checks for a meeting note

As a Context user, I want the appointment page to show that it is working when I click Create a new note, so that a slow check never looks like a dead button.

Category: ux

## What

In AppointmentPanel.razor, clicking Create a new note first runs MeetingNoteExists; while that runs (checkingLink), repeat clicks are swallowed with no feedback. Show a busy state on the link (for example disabled with aria-busy and a short status) until the check returns, and make sure the field or the meeting note still opens afterwards.

## Why

The check is a local file lookup and normally fast, but on a slow or encrypted data folder the button would appear to do nothing, which is the same symptom as the bug fixed in #494.

## Notes

Raised by the ux review of job 395-new-note-after-appointment (factory/jobs/395-new-note-after-appointment/round-1/review-ux.md).
