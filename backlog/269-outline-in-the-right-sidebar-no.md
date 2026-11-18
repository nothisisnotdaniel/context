# 269 Outline in the right sidebar, no reformat notice

## What

In the desktop app (src/Context.Desktop/NoteEditor.razor, OutlinePanel.razor, Main.razor, app.css): (1) When the open note has an outline (headings), show it in the right sidebar instead of above the note text; clicking a heading still jumps to it. With no headings, or with no note open, the right sidebar shows no outline part. The spec decides where in the right sidebar it goes (for example above the todos) and how it behaves in a narrow window. (2) Remove the grey 'Saving will reformat parts of this note.' line above the editor. Update the affected tests. Light and dark mode.

## Why

The outline at the top pushes the note text down, and the reformat line is noise the user doesn't want to read every time a note opens.

## Notes

Requested by the user on 2026-11-18.
