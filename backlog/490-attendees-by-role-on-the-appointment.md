# 490 Attendees by role on the appointment page

As someone preparing a meeting, I want to see who organizes it and who is required or optional, so that I know who to talk to.

Category: ui

## What

`IcsParser` also reads `ORGANIZER`'s `CN` and each `ATTENDEE`'s `ROLE` (and Outlook's organizer and required/optional lists); the appointment page shows the organizer first, then required, then optional attendees, each as a person link. Addresses are still never read out.

## Why

Context shows attendee names only. assistant shows attendees by role.

## Notes

Idea from Creacility/assistant PLAN.md (compared on 2026-11-22).
