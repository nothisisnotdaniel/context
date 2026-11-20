# 244 Edit a note in place: no repeated title

As a Context user, I want an open note to look the same whether I am reading or editing it, so that the page is calm and the title appears only once.

## What

In the desktop app's note editor (NoteEditor.razor, the ProseMirror editor in src/Context.Desktop/editor, editor.css, app.css): (1) The page title above the editor (the note's name) is the note's only level-1 heading. A first line `# <title>` in the note is not shown again inside the editor; it stays in the file, so saving writes it back unchanged and nothing is lost (the lossless check keeps working). (2) Every heading inside the editor is level 2 or below: typing `# ` makes a level-2 heading, and the heading menu or shortcuts offer no level 1. How a level-1 heading further down an existing note is shown and saved is for the spec to decide, without changing the file unless the user edits that heading. Light and dark mode.

## Why

The screenshot of an open note shows the title twice (page title and the note's own `#` heading) and the text sits in a rounded box, which looks like a form instead of a note.

## Notes

Requested by the user on 2026-11-16 after job 193, from the editor screenshot. New notes created by the app may keep writing `# <title>` as their first line; it is just never shown twice. Removing the editor box was split out to #249 on 2026-11-16 (cheap mode).
