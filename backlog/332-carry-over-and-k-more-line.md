# 332 Carry-over: "… and k more" line is not an open point

As someone who keeps recurring meeting notes, I want the "… and k more" line under "From last time" to read as a note, not as an open point, so that the new meeting's open points list only real items.

Category: bug

## What

MeetingCarryOver (src/Context.Core/MeetingCarryOver.cs) writes the "… and k more" overflow line as a list item, so it appears as an open point in the new meeting note. Write it as plain text (or an italic line) below the list instead, and update the spec'd tests.

## Why

UX review of job 245: the line is harmless but looks like an open point to carry over again next time; the builder left it because the job 245 spec fixed its form.

## Notes

Proposed by job 245-agenda-carry-over (review-ux 1).

Proposed by job 245-agenda-carry-over (`factory/jobs/245-agenda-carry-over/`).
