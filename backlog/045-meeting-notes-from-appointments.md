# 045 Meeting notes from appointments

As a Context user, I want to create a meeting note straight from an appointment, so that the title, time and attendees are filled in for me.

## What

Desktop app, plus a CLI command where it fits. `/meeting <n>` creates a note for appointment `<n>` in `/current`, named from its date and title (cleaned for a file name), with its title, time and attendees and the tag `#meeting`. If that note exists, open it instead.

## Why

Meeting notes are among the most frequent notes, and copying the details over by hand is tedious. Opening an existing note instead keeps one note per meeting.
