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
- [x] Tags: parse tags in Markdown files and store the links between files in the database → 022-tags
- [x] Tags with spaces: support tags that contain spaces. Suggested notation: `[[tag with spaces]]`, but the spec writer may propose other options (and should weigh them against `[[...]]` being read as a wiki link elsewhere). Single-word `#tags` keep working, and both forms of the same tag should count as one tag. → 024-tags-with-spaces
- [x] /current view: show today's due todos and linked notes → 026-current-view
- [ ] Calendar: pull in today's appointments and show them in /current
- [ ] Email: pull in information from emails
- [ ] Todo dates with years: the sidebar shows due dates as `MM-dd` with no year. Show the year when a todo is due in a year other than the current one (e.g. `2027-01-15`), so todos far ahead or long overdue can't be mistaken for this year's.
- [ ] Helpful empty todo sidebar: "No todos yet" doesn't say how to add one. Mention `/todo` in the empty sidebar, e.g. "No todos yet — add one with /todo".
- [ ] Faster tag parser: `TagParser.SkipCodeSpan` rescans the rest of the line for every unmatched backtick run, so a line with many unmatched backticks takes O(n²) time. Make the scan linear before email or calendar text gets indexed.
- [ ] Note size limit for tag sync: `TagIndex.Sync` reads each changed note whole with no size limit. Skip (or only partly read) notes above a sensible size and say so in `/tags`.
- [ ] Shorter tag help: shorten the `/tags` help description (e.g. "List tags: /tags [tag]") and give the `/links` usage hint an example (`/links Meeting`).
- [ ] Note links: `[[Note name]]` in a note links to the note with that name (matched like `/open`: ignoring case, optional `.md`). Store these links in the database next to the tag links, and show them in `/links <note>` (both notes it links to and notes that link to it). A link to a note that doesn't exist yet is kept and shown as missing. Keep `#[tag]` for tags with spaces (job 024).
- [ ] Mention `#[...]` in tag help: the `/tags` help text and the empty-tags message only mention `#tag`. Also show the `#[tag with spaces]` form.
- [ ] Merge duplicate whitespace code: the whitespace-collapsing loop exists twice, in `NormalizeBracket` and `CollapseWhitespace`. Keep one.
- [ ] Check `/tags` input: `/tags <tag>` doesn't check what was typed against the tag rules, so bad input just shows "No notes tagged". Show a hint that says what a valid tag looks like instead.
- [ ] Spacing in /current: add a blank line between the "Today, <date>" title, the Due table, and the Linked notes table (or its "No linked notes" / "Can't read context.db" line).
- [ ] Limit /current rows: `/current` shows every due todo and runs one database query per distinct tag, with no limit. Cap the Due table (last row `… N more`, like the sidebar) and the number of tags looked up.
- [ ] Simplify /current code: `CurrentCommands` builds the due list and the tag collection in two passes; do it in one loop.
