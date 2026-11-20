# 305 People page opened from a person icon in the left sidebar

As a desktop user, I want a People page that I open from a person icon in the left sidebar, so that I can find everyone I keep notes on without digging through the note list.

## What

Add an icon-only button with a person icon (aria-label and title 'People') to the left sidebar's .notes-bar-buttons in src/Context.Desktop/NotesPanel.razor, between the Triage (inbox) button and the Tags button, so the order is Triage, People, Tags, Settings. It opens a People page in the main region, like the Tags page does: a list of every person page (the people/<Name> notes that PersonParser and the people aliases use), sorted by name, each opening that person's page (PersonPanel). Show a clear empty state when there are no people yet. Keyboard and screen reader friendly, styled like the other icon buttons and pages, light and dark mode.

## Why

User request on 2026-11-18: the list of people should be its own page, reached from a person icon placed between the tag and inbox icons.

## Notes

Requested by the user on 2026-11-18. Needed before the left sidebar stops showing the note hierarchy, because person pages are now reached through that hierarchy.
