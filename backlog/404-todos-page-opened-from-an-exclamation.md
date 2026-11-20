# 404 Todos page opened from an exclamation-mark icon in the left sidebar

As a desktop user, I want a Todos page that I open from an exclamation-mark icon in the left sidebar, so that I can see and work through all my open todos on a full-width page instead of the narrow sidebar.

## What

Add an icon-only button with an exclamation-mark icon (aria-label and title "Todos") to the left sidebar's .notes-bar-buttons in src/Context.Desktop/NotesPanel.razor, next to the People and Tags buttons. It opens a Todos page in the main region, like the People and Tags pages: every open todo (todos.json and todos inside notes, including meeting todos) sorted by due date, overdue first, then undated; each shows its text, due date and the note or meeting it comes from (click to open it), and can be marked done there. Show a clear empty state when there are no open todos. Keyboard and screen reader friendly, styled like the other icon buttons and pages, light and dark mode. The existing todo sidebar stays.

## Why

User request on 2026-11-20: the todo sidebar is too narrow to work through todos, so they need their own view reached from an exclamation-mark icon in the left sidebar.
