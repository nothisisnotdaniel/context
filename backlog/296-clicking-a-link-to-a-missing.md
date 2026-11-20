# 296 Clicking a link to a missing note creates the note

As a desktop user, I want clicking [[New topic]] for a note that doesn't exist yet to create that note and open it, so that I can start new notes straight from the one I'm writing.

## What

Clicking a [[Name]] note link whose note does not exist now shows 'No note named <Name>.' (NoteEditor.razor around the link-click handler, DesktopActions.NoNoteNamedText). Instead, create the note <Name> (with any missing parent pages for a subpage name such as Projects/Alpha), save the current note first as other navigation does, and open the new one. Keep the existing checks for names (length, refused characters) and show a clear status if creation fails. A [[Note#Heading]] link to a missing note creates the note too, without the heading. [[#Heading]] in the same note is unchanged.

## Why

User report on 2026-11-18: using [[ shows that no such page exists; it should create a new note, the way @[ makes a person page.

## Notes

Requested by the user on 2026-11-18.
