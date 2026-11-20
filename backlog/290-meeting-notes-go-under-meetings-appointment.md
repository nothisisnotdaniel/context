# 290 Meeting notes go under Meetings/<appointment>/<date>

As a desktop user, I want every meeting note filed as Meetings/<appointment title>/<date>, so that all notes of a recurring meeting sit together in one place.

## What

MeetingNote (src/Context.Core/MeetingNote.cs) now creates the top-level note 'yyyy-MM-dd <title>'. Create it instead as the subpage 'Meetings/<title>/yyyy-MM-dd' (NoteStore subpage names use '/'), creating the 'Meetings' and 'Meetings/<title>' parent pages if they are missing. Keep the #meeting tag, the filled-in content, the one-note-per-meeting rule (an existing note is found and never changed) and the cleaning of characters that are refused in file names. Every path that makes a meeting note uses it: /meeting <n>, the appointment page's 'Create a new note' link and any other caller. Decide and state in the spec what happens to existing 'yyyy-MM-dd <title>' notes (for example: left where they are, still found as that meeting's note).

## Why

User review on 2026-11-18: notes of appointments should always go into the hierarchy Meetings/<name of appointment>/date.

## Notes

Requested by the user on 2026-11-18. Title length limits: the name now spends 'Meetings/' and '/yyyy-MM-dd' instead of 'yyyy-MM-dd '.
