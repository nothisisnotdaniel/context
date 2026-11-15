# Context

A personal app for keeping notes, todos and the day's appointments in one place, with a desktop app and a
scriptable command-line tool, `context`.

## Command line

`context help` lists every command. Results go to stdout, errors to stderr as one line. Exit codes: 0 success,
1 error, 2 usage error.

### Subpages

A note can have subpages: the children of `Meetings.md` are the `.md` files in the `Meetings/` folder next to it, to
any depth. A subpage's name is its path with `/` on every OS, so `notes` lists `Meetings/Meeting 1` right after
`Meetings`, and `note show`, `links` and `export` take that name (quote it when it has spaces):

```sh
context note show "Meetings/Meeting 1"
```

The name is only matched against the note list, never used as a file path. `import` and `capture` write top-level
notes only; subpages are created, moved and deleted in the desktop app.

### People

Write `@[Anna Schmidt]` in a note to mention a person (at the start of a line or after a space; not in code).
`context people` lists everyone mentioned, with the number of notes that mention them. `context person Anna Schmidt`
(or `context person "@[Anna Schmidt]"`) lists those notes, then the appointments whose title or location mentions
the person or that list them as an attendee, latest first, then the open todos that mention them and the done ones
(latest done first). Appointments come from `calendar/*.ics` and the last
downloaded copy of the Exchange calendar; nothing is downloaded. In the desktop app, clicking a mention (in a note or
in the todo list) opens the person's page `people/Anna Schmidt`, creating it (and `people`) on that click if needed.
A done todo stays in `todos.json` with a `done` field, so the person's page still lists it.

Give a person shorthands with an `aliases::` line on their page, for example `aliases:: Anna, AS` on
`people/Anna Schmidt`: then `@[Anna]` and `@[AS]` in any note count as mentions of Anna Schmidt, in
`context people`, `context person` (`context person AS` works too) and the desktop app. A person's full name always
wins over an alias. An alias that two people share links to no one: `context person Anna` then lists only the
`@[Anna]` mentions and adds a hint on stderr, clicking such a mention in the desktop app shows the hint instead of
making a page, and both person pages warn about it. Alias changes count from the next run.

### Attributes and page types

Give any page attributes with plain lines that start with a key, then `::`, then the value:

```markdown
# Anna Schmidt

affiliation:: [[ACME]]
role:: Bereichsleiter
```

A key starts with a letter (then letters, digits, `-`, `_`; at most 50) and is matched ignoring case; the line must
start at the beginning (no indent, not in code). A line with no value, such as `role::`, is a placeholder and finds
nothing. A value that is one `[[link]]` is also a note link, so `ACME` lists Anna under `linked from`.
`context find role Bereichsleiter` prints the names of the notes with that attribute, one per line (none: no output);
values match ignoring case, and `[[ACME]]`, `ACME` and `acme` are the same value.

A page's types are its `type::` lines (`type:: organization`); a page under `people` without one is a `person`.
`context find type person` lists them. `context types` prints each type with its attributes (`person` comes with
`aliases`, `affiliation` and `role`, `organization` with none). Define your own type as a subpage of a top-level note `types`,
for example `types/project` with the lines `attribute:: owner` and `attribute:: due`; a `types/person` page replaces
the built-in one. A new person page made by clicking a mention starts with one empty line per `person` attribute.
In the desktop app, `/find role Bereichsleiter` in the command palette (Ctrl/Cmd+K) lists the same notes.

### Daily note

`context today` prints today's note, named after the date (for example `2026-10-29`). When it doesn't exist yet it
is created first with the day's appointments and the todos due today or earlier; an existing note is printed as it
is and never changed. In the desktop app, `/today` in the command palette opens the same note.

### Meeting notes

`context current` numbers today's appointments (`1`, `2`, ...). `context meeting 2` prints the note for appointment 2,
named after its date and title (for example `2026-10-29 Review Q4 plan`). When it doesn't exist yet it is created first,
tagged `#meeting`, with the appointment's title, time, location and attendees; an existing note is printed as it is and
never changed. In the desktop app, the current view shows the same numbers and `/meeting 2` in the command palette
opens the same note.

### Note templates

Keep notes you write again and again (meetings, reviews) as plain Markdown files in the `templates` folder of the
data folder, for example `templates/meeting.md`. `context new Weekly sync --template meeting` creates the note
`Weekly sync` from it: every `{{date}}` becomes the date (`yyyy-MM-dd`), every `{{time}}` the time (`HH:mm`) and every
`{{title}}` the note's title; anything else stays as written, and no `# title` line is added. `context new <title>`
without `--template` creates a note with just its title. A template name is only matched against the files in
`templates/` (ignoring case), never used as a path; a name that isn't there prints the templates you have. In the
desktop app, `/new Weekly sync --template meeting` in the command palette does the same and opens the note.

### JSON output

Add `--json` to a read command (`version`, `todos`, `notes`, `note show`, `tags`, `links`, `people`, `person`,
`find`, `types`, `current`) to get one
JSON object on one line instead of text. Property names are fixed camelCase; missing values are `null`; dates are
`yyyy-MM-dd` and times ISO 8601 with offset. Errors stay one text line on stderr with the same exit codes, and
write commands and `help` refuse the flag.
Non-ASCII text and characters such as `+`, `&`, `'` and `<` are written as `\uXXXX` escapes, so the output is
plain ASCII; any JSON parser, `ConvertFrom-Json` included, reads them back.

| Command | Shape |
|---------|-------|
| `version` | `{"version":"1.2.3"}` |
| `todos` | `{"todos":[{"due":"2026-11-14","text":"..."}]}` |
| `notes` | `{"notes":[{"name":"A","locked":false}]}` |
| `note show <name>` | `{"name":"A","text":"..."}` |
| `tags` | `{"tags":[{"tag":"work","count":2}]}` |
| `tags <tag>` | `{"tag":"work","notes":["A"]}` |
| `links <note>` | `{"note":"A","links":[{"note":"B","direction":"to","missing":false,"sharedTags":["work"]}]}` |
| `people` | `{"people":[{"person":"Anna Schmidt","count":2}]}` |
| `person <name>` | `{"person":"Anna Schmidt","notes":["A"],"appointments":[{"start":"...","end":"...","allDay":false,"title":"...","location":null}]}` |
| `find <key> <value>` | `{"key":"role","value":"Bereichsleiter","notes":["people/Anna Schmidt"]}` |
| `types` | `{"types":[{"type":"organization","attributes":[]},{"type":"person","attributes":["aliases","affiliation","role"]}]}` |
| `current` | `{"today":"...","appointments":[{"start":"...","end":"...","allDay":false,"title":"ACME kickoff","location":null,"related":[{"name":"projects/ACME Rollout","tags":["acme"],"words":["acme"]}]}],"calendar":{...},"due":[...],"linkedNotes":[...]}` |

PowerShell examples:

```powershell
# Todos due this week (due is ISO yyyy-MM-dd, which the [datetime] cast reads the same in every culture)
$todos = (context todos --json | ConvertFrom-Json).todos
$todos | Where-Object { [datetime]$_.due -le (Get-Date).AddDays(7) } | ForEach-Object { "$($_.due) $($_.text)" }

# Notes you can read here (not sensitive)
(context notes --json | ConvertFrom-Json).notes | Where-Object { -not $_.locked } | Select-Object -ExpandProperty name

# Today's appointments as start and title
$current = context current --json | ConvertFrom-Json
$current.appointments | ForEach-Object { "$($_.start) $($_.title)" }
if ($null -eq $current.due) { Write-Warning 'todos.json could not be read' }

# Meeting prep: the notes related to each of today's appointments
$current.appointments | ForEach-Object { "$($_.title): $(($_.related | ForEach-Object { $_.name }) -join ', ')" }

# The text of one note
(context note show Plan --json | ConvertFrom-Json).text

# Everyone at ACME
(context find affiliation ACME --json | ConvertFrom-Json).notes
```

`current` writes its document even when part of the data can't be read: then it exits 1, its error lines are on
stderr, and `due` or `linkedNotes` is `null` (and each appointment's `related` when `context.db` can't be read).

`related` lists up to three notes per appointment that share a tag or a title word with the appointment's title or
attendee names (meeting prep): `tags` are the matching tags (raw, without `#`), `words` the shared title words. Only
`current` has it; the appointments of `person` don't. In text mode `context current` prints each as
`related<TAB><name><TAB><matches>` right after its appointment line.
