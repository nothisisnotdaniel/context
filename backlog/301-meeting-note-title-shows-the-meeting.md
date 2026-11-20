# 301 Meeting note title shows the meeting name, not only the date

As a desktop user, I want a meeting note to show which meeting it belongs to when it is open or listed on its own, so that a note titled only '2026-11-15' is still recognisable.

## What

Since job 220, meeting notes are Meetings/<title>/yyyy-MM-dd and their '# ' heading is only the date; the meeting name lives on the parent page. Decide where the meeting name should still show (for example the note heading '# 2026-11-15 Budget', search results, the related-notes list, the snapshot) and make it so without changing the note's place in the hierarchy.

## Why

The UX review of job 220 noted that the new note's title shows only the date, so outside the breadcrumb the user can't tell which meeting it is.

## Notes

Proposed by job 220-meeting-notes-hierarchy (review-ux nit 1).

Proposed by job 220-meeting-notes-hierarchy (`factory/jobs/220-meeting-notes-hierarchy/`).
