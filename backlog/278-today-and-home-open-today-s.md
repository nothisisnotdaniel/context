# 278 Today and Home open today's note in the editor

As a desktop user, I want the Today/Home button and app start to open today's daily note in the editor, so that there is one place for today instead of a separate dashboard that looks like a note but is not.

## What

The Today/Home button and app start open today's daily note (yyyy-MM-dd) in the page editor, like /today and Pick a day: created with DailyNote.Compose when missing, with the same unsaved-changes rules as PickDay. Remove the CurrentPanel dashboard as a page. Move End the day into the More actions menu of today's note. Related notes per appointment and the feed status move to the right sidebar via the Today's appointments item; the spec decides where the Exchange feed offline/failed line goes. Update tests, CLAUDE.md and README.

## Why

The user asked on 2026-11-18: Today/home should go to todays note. The dashboard and the daily note show the same appointments and todos, which is confusing.

## Notes

Requested 2026-11-18. Build after the Today's appointments in the right sidebar item, so the related notes keep a home.
