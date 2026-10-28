# Context backlog

`/factory next --project Context` runs the first unchecked item, checks it off and records the job id.
Write one feature per line, in plain language.

- [x] App shell: Spectre.Console layout with an input box at the bottom for navigation → 007-app-shell
- [x] Command popup: typing `/` in an empty input box opens a popup just above the input box that lists all registered commands with their short descriptions. Each further key filters the list to the commands that match what was typed, ranked so that typos still find the right command (Levenshtein distance, e.g. `/hepl` still shows `/help`). The popup closes on Enter, Esc, or when the line no longer starts with `/`. Same behaviour as the Times command popup (job 006-command-popup); describe how the popup is drawn in CLAUDE.md. → 010-command-popup
- [x] Notes: create, list and open Markdown files in the Context app data folder → 012-notes
- [x] Note titles ending in .md: `/new x.md` currently saves `x.md.md`, which `/open` can't find. Strip a trailing `.md` from the title (or reject it) so every created note can be opened. → 013-md-note-titles
- [x] Safe note text: replace terminal control characters in note text and in note names read from disk with a visible placeholder before rendering, so escape sequences never reach the terminal. Needed before email and calendar import. → 015-safe-note-text
- [x] Windows-safe note titles: reject Windows reserved names (CON, PRN, AUX, NUL, COM1-9, LPT1-9) and titles ending in `.` or a space. → 018-windows-safe-titles
- [x] Todos: add todos with due dates, shown in a sidebar sorted by due date → 020-todos
- [ ] Tags: parse tags in Markdown files and store the links between files in the database
- [ ] /current view: show today's due todos and linked notes
- [ ] Calendar: pull in today's appointments and show them in /current
- [ ] Email: pull in information from emails
