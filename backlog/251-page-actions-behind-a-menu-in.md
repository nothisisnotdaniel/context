# 251 Page actions behind a … menu in the top bar

As a Context user, I want the buttons each page shows (for example Rename, Delete, History, Move, Export with an open note) hidden behind one … icon, so that the page shows my content instead of a row of buttons.

## What

In the desktop app (src/Context.Desktop): add a '…' icon button (aria-label and title 'More actions', aria-haspopup and aria-expanded) at the right end of the top bar, after Today and Pick a day. It opens a menu with the actions of the page shown now, in place of the separate buttons that page shows today: with an open note at least Rename, Delete, History, New subpage, Move, Open in default editor, Export and Mark sensitive (Main.razor), and the spec lists the action buttons of the other pages (day view: End the day; Triage, Tags, Settings, History and the other panels) and decides per button whether it moves into the menu or stays because it is the page's main control (for example the Triage choices and the × close stay). Choosing an item does exactly what the button did, including its confirmation (Delete) and its inline form (Rename, Move, New subpage show their text box as today). The menu works from the keyboard (arrow keys, Return, Esc closes and returns focus to the … button) and closes on clicking outside. When the shown page has no actions, the … button is hidden or disabled. The Save button of the note editor is left as it is (the auto save item decides its future). Light and dark mode.

## Why

Every page shows its own row of buttons (eight above an open note), which makes the window busy; one menu in a fixed place is calmer and easier to find.

## Notes

Requested by the user on 2026-11-16 after the screenshots of job 196.
