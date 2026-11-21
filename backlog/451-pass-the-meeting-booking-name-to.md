# 451 Pass the meeting booking name to Times as one argument

As a user booking a meeting in Times, I want the booking name passed as one fixed argument, so that a future change to Times' checkpoint grammar can never read part of a meeting title as a project or option.

Category: security

## What

Context's `meeting --book` and the desktop booking pass the name to `tm checkpoint` as free text that Times re-joins with spaces (`MeetingNote.BookingName`, `MeetingNote.BookingArguments`). Agree a Times-side form of `checkpoint` that takes the name as a single argument (for example after `--`), and use it from Context.

## Why

Review-security of job 381: today Times' grammar only reads a project or an option from the start, so nothing harmful follows, but a Times-side single-argument boundary would be sturdier if the grammar grows.

## Notes

Proposed by job 381-meeting-flow (`factory/jobs/381-meeting-flow/`).
