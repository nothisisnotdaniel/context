# 453 Explain a changed meeting booking name

As a user whose meeting title starts with # or -, I want to see why the booked name differs from the title, so that `05 Weekly` for `#05 Weekly` doesn't look like a mistake.

Category: ux

## What

When `MeetingNote.BookingName` drops a leading `#`, `-` or spaces, add a hint to the `Booked <H:mm> <name> in Times.` line (CLI and desktop), for example `(leading # removed so Times doesn't read it as a project)`.

## Why

Review-ux of job 381: the change is deliberate and the Booked line shows the name actually used, so it is not misleading, but the reason is not visible.

## Notes

Proposed by job 381-meeting-flow (`factory/jobs/381-meeting-flow/`).
