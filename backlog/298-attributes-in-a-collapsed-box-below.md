# 298 Attributes in a collapsed box below the note title

As a desktop user, I want a page's attribute lines (aliases::, affiliation::, role:: and the rest) in a collapsed box right below the title, so that the note body starts with what I wrote and the attributes are still one click away.

## What

Attribute lines ('key:: value', AttributeParser in src/Context.Core, including type:: and the empty lines a new person page starts with) now show as plain text in the note body. Show them in a box right below the note title (NoteEditor.razor) that is collapsed by default, with a summary such as 'Attributes (3)', and expanding it shows each key and value. They must stay editable there (change a value, add and remove an attribute), and saving keeps writing them to the note as 'key:: value' lines in the same place and format, so the CLI, find, people aliases and page types read them as before. Hide those lines from the editor body. A note without attributes shows no box. Keyboard and screen reader friendly (a native details/summary or equivalent), light and dark mode. The spec decides which lines count (for example only a block of attribute lines at the top of the note, or every attribute line) and states it.

## Why

User review on 2026-11-18: attributes like aliases and affiliation should be in a collapsed box right below the title.

## Notes

Requested by the user on 2026-11-18. Touches the editor's markdown round trip (editor/src/markdown.js), so the saved text must stay byte-equal for notes whose attributes weren't edited.
