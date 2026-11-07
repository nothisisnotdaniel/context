# 103 Meeting flow

As a developer, I want to turn a calendar meeting into a note with action items, so that meeting notes, follow-up todos and meeting time are handled in one step.

## What

`context meeting [<time or title>]` picks today's calendar event (the current or next one by default) and creates a note from a template: title, time, attendees, and sections for Notes, Decisions and Actions. `- [ ]` lines under Actions become todos as usual, and an `AB#1234` in the title or notes links the meeting to that work item. When Times is installed, it offers to book the meeting as a checkpoint through `times` one-shot commands; nothing is booked without a yes. Full path.

## Why

Meetings produce notes, todos and time to book, and today each lands in a different place, or nowhere. One step after the meeting makes all three happen.
