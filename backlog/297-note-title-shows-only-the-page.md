# 297 Note title shows only the page's own name

As a desktop user, I want the title above a note to show only the page's own name, so that it doesn't repeat the hierarchy the breadcrumb already shows.

## What

The note editor's title (src/Context.Desktop/NoteEditor.razor, h2.note-title, DisplayName = the full name) shows the whole path, for example 'a/b/c', right below the breadcrumb that already shows a / b / c (Main.razor). Show only the last part ('c') in the title. Keep the full name in the title attribute (tooltip) and wherever the full name is needed to tell notes apart (status lines, error messages such as 'Can't read …'). A top-level note looks the same as now. Light and dark mode.

## Why

User review on 2026-11-18: the title below the breadcrumb repeats the hierarchy; it should show only the plain page name, e.g. a/b/c in the breadcrumb and only c in the title.

## Notes

Requested by the user on 2026-11-18.
