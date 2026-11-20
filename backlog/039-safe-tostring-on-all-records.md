# 039 Safe ToString on all records

As a Context user, I want every record that holds my text to hide it in `ToString()`, so that note text never ends up in logs or error messages by accident.

## What

Give the records inside the views (`Appointment`, `NoteLink`, `TagCount`) and the other Core records holding raw text (`TodoItem`, `NoteInfo`, `Note`, `CalendarDay`, `CalendarFeedResult`, `MigrationResult`) the same `ToString() => nameof(<Type>)` override as the views (job 042), with tests.

## Why

The views already do this since job 042, but other Core records with raw text don't, so a stray log line could leak content.
