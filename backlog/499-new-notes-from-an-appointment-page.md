# 499 New notes from an appointment page go under that appointment

As a Context user, I want a note I create from an appointment page to be stored under that appointment, so that my notes for a meeting series stay together in the hierarchy instead of at the top level.

Category: bug

## What

Since job 395 (#494), when the appointment's meeting note already exists, the appointment page's Create a new note opens a Note title field and creates a plain top-level note through CreateNote. Instead, create it as a subpage of the appointment page Meetings/<title part> (the parent of the dated meeting notes Meetings/<part>/yyyy-MM-dd), through CreateSubpage, so it becomes Meetings/<part>/<title>. Create the Meetings and Meetings/<part> pages first when they are missing, the same way MeetingNote does. Keep the existing title rules and texts; a clash with an existing sibling (including a yyyy-MM-dd dated note) is refused with the usual text. Open the new note afterwards as today. Add a test that fails before the change.

## Why

The user reported after #494 that a note added from an appointment page is not stored in the correct hierarchy: it lands at the top level, so notes about a meeting are scattered and hard to find later.

## Notes

Reported on 23 Nov 2026 after job 395-new-note-after-appointment. The user chose: under the appointment page (Meetings/<part>/<title>), not under today's dated meeting note and not a general rule for the notes bar or palette.
