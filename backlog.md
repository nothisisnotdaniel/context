# Context backlog

`/factory next --project Context` runs the first unchecked item, checks it off and records the job id.
Write one feature per line, in plain language.

- [x] App shell: Spectre.Console layout with an input box at the bottom for navigation → 007-app-shell
- [x] Command popup: typing `/` in an empty input box opens a popup just above the input box that lists all registered commands with their short descriptions. Each further key filters the list to the commands that match what was typed, ranked so that typos still find the right command (Levenshtein distance, e.g. `/hepl` still shows `/help`). The popup closes on Enter, Esc, or when the line no longer starts with `/`. Same behaviour as the Times command popup (job 006-command-popup); describe how the popup is drawn in CLAUDE.md. → 010-command-popup
- [x] Notes: create, list and open Markdown files in the Context app data folder → 012-notes
- [ ] Todos: add todos with due dates, shown in a sidebar sorted by due date
- [ ] Tags: parse tags in Markdown files and store the links between files in the database
- [ ] /current view: show today's due todos and linked notes
- [ ] Calendar: pull in today's appointments and show them in /current
- [ ] Email: pull in information from emails
