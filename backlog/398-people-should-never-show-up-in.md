# 398 People should never show up in the recent notes list

As a Context user, I want person pages kept out of the recent notes list and shown without a page hierarchy, so that the sidebar lists only the notes I work on, and a person stays a single flat page.

Category: bug

## What

- **Recent notes:** a person page (`people/<Name>`, `People.PersonOf`) is never listed in the sidebar's recent notes list, even right after I open it. The list still shows up to `RecentNotes.MaxCount` other notes. Opening a person page doesn't push another note out of the list.
- **No grey parent:** wherever a person page appears in a list of notes (for example search results), it shows the person's name only, without the grey `people` parent after it.
- **No Subpages section:** a person page never shows the `Subpages` list below the editor, even if files exist in its folder.
- **No New subpage or Move:** a person page's More actions menu has neither `New subpage` nor `Move`, so a person page can't get children or be moved out of `people`.
- The top-level `people` page itself and the People panel stay as they are.

## Why

Person pages are reached from the People panel and from `@[Name]` mentions. In the recent list they crowd out the notes I'm actually writing. Their `people/` parent is a storage detail, not a structure I use, so showing it, or letting a person page have subpages, only adds noise.

## Notes

- 2026-11-19: Draft from ControlRoom.
- 2026-11-20: Story written with the user. The user chose all three hierarchy parts: the grey parent, the Subpages section, and New subpage / Move.
- The spec decides whether entries already in `recent.json` that name a person page are dropped when the list is read, or only hidden.
- Code: `RecentNotes.cs` and `NotesPanel` (recent list, job 240), the note-page Subpages nav (job 238), the More actions menu (`New subpage`, `Move`), `People.PersonOf`.
