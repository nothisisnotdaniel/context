# 306 Left sidebar shows the 10 most recently opened notes instead of the hierarchy

As a desktop user, I want the left sidebar to list only the 10 notes I opened most recently, so that the notes I'm working with are one click away and the sidebar stays short.

## What

The left sidebar's note list (src/Context.Desktop/NotesPanel.razor, ul.note-list with expandable children) now shows the whole note hierarchy. Replace it with a list of the last 10 notes the user opened, most recent first, with no hierarchy or expand toggles (show subpage names so that notes with the same last part can be told apart). Opening a note moves it to the top; renaming or deleting a note updates or drops its entry; a note that no longer exists is left out. The list survives a restart: the spec decides where it is kept (for example a small file under the notes root or context.db) and states the format. Keep the New note field and saved searches. Notes not in the list stay reachable through search, links, the command palette and the People and Tags pages. Light and dark mode, keyboard and screen reader friendly.

## Why

User request on 2026-11-18: the left sidebar should only show the last 10 recently opened notes, not the hierarchy.

## Notes

Requested by the user on 2026-11-18. Done after the People page, so person pages stay reachable.
