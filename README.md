# Context

A personal app for keeping notes, todos and the day's appointments in one place, with a desktop app and a
scriptable command-line tool, `context`.

## Command line

`context help` lists every command. Results go to stdout, errors to stderr as one line. Exit codes: 0 success,
1 error, 2 usage error.

### Subpages

A note can have subpages: the children of `Meetings.md` are the `.md` files in the `Meetings/` folder next to it, to
any depth. A subpage's name is its path with `/` on every OS, so `notes` lists `Meetings/Meeting 1` right after
`Meetings`, and `note show` and `export` take that name (quote it when it has spaces):

```sh
context note show "Meetings/Meeting 1"
```

The name is only matched against the note list, never used as a file path. `import` and `capture` write top-level
notes only; subpages are created, moved and deleted in the desktop app.

### People

Write `@[Anna Schmidt]` in a note to mention a person (at the start of a line or after a space; not in code).
The desktop app's people list shows everyone mentioned, with the number of notes that mention them, and a person's
page lists those notes, then the appointments whose title or location mentions the person or that list them as an
attendee, latest first, then the open todos that mention them and the done ones (latest done first). Appointments
come from `calendar/*.ics` and the last downloaded copy of the Exchange calendar. In the desktop app, clicking a
mention (in a note or in the todo list) opens the person's page `people/Anna Schmidt`, creating it (and `people`)
on that click if needed. A done todo stays in `todos.json` with a `done` field, so the person's page still lists it.

Give a person shorthands with an `aliases::` line on their page, for example `aliases:: Anna, AS` on
`people/Anna Schmidt`: then `@[Anna]` and `@[AS]` in any note count as mentions of Anna Schmidt in the desktop
app's people list and on her page. A person's full name always wins over an alias. An alias that two people share
links to no one: clicking such a mention in the desktop app shows a hint instead of making a page, and both person
pages warn about it. Alias changes count from the next run.

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
`context find type person` lists them. Each type has attributes (`person` comes with
`aliases`, `affiliation` and `role`, `organization` with none). Define your own type as a subpage of a top-level note `types`,
for example `types/project` with the lines `attribute:: owner` and `attribute:: due`; a `types/person` page replaces
the built-in one. A new person page made by clicking a mention starts with one empty line per `person` attribute.
In the desktop app, `/find role Bereichsleiter` in the command palette (Ctrl/Cmd+K) lists the same notes.

### Full-text search

`context search budget 2027` prints every note whose text contains `budget 2027` (ignoring case, anywhere in a word),
one line per note: its name, the number of the first line that contains the text and that line, separated by tabs
(none: no output). The words are joined with one space and matched as one run of text, not as separate words; the
text needs at least 3 and at most 200 characters. Sensitive notes are never searched. In the desktop app,
`/search budget 2027` in the command palette lists the same notes; click one to open it.

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

### Day's end

`context dayend` writes the day's worked hours from the Times app into today's daily note (a `## Worked` section) and
moves the todos due today or earlier to tomorrow, listing them in both daily notes. `context dayend 2026-11-14` closes
that day instead. Times' data folder is `TIMES_DATA_DIR` when set (an absolute path), else `Times` in the app data
folder; it is only read. The desktop app has an `End the day` button in the current view, and it closes the day by
itself after you book your leaving in Times (the todos move once, at a leaving at 17:00 or later).

### Note history

The desktop app keeps the previous text of a note as a version every time you save a change in it. The versions sit
in a `.history` folder next to the note (`.history/<title>/`), at most 50 per note and 25 MB in total; the oldest go
first. `History` in a note's toolbar lists them, shows how each differs from the saved text, and `Restore this
version` puts one back after asking (the current text is kept as a version too). Renaming or moving a note takes its
history along; deleting the note removes it. Captures, the day's end, the briefing, imports and the command line keep
no versions. A sensitive note's versions stay sealed; making it plain removes them.

- After a sensitive note is renamed, its old title stays readable in the file names of its older versions
  (`.history/<new>/<id>~<old>.ctxs`) until they are pruned or the note is deleted, just as a sensitive note's own file
  name is readable.
- A version's path holds the note's title twice (file names of about 130 characters), so on Windows a long data
  folder path can hit the path length limit; the save then fails with `Can't keep the old version of <name>. Nothing
  was saved.`
- When `.history` in a folder is broken (for example a file or a link called `.history`), every save of the notes in
  that folder fails with the same message. Remove that `.history` entry from the data folder to fix it.

### Attachments

Drop images (PNG, JPEG, GIF, WebP) or PDF files onto an open note in the desktop app to attach them, at most 10
files of up to 10 MB each per drop. The app checks each file's type from its bytes, never from its name, and stores it
in the `.attachments` folder of the note's level (next to the note file), under a cleaned name (`My Shot (1).PNG`
becomes `My-Shot-1.png`; a name already taken gets `-2`, `-3` and so on). It inserts a link at the drop position:
`![shot.png](.attachments/shot.png)` for an image, which shows in the note, and `[plan.pdf](.attachments/plan.pdf)`
for a PDF, which opens in the system's PDF viewer when you click it. The note is not saved by the drop; save it to
keep the link. Sensitive notes can't have attachments.

Moving a note to another level takes the attachments it links along into the new level's `.attachments` folder
(renamed with `-2` and its links updated when the name is taken there). An attachment that another note in the old
level still links is copied, so both notes keep working. If anything goes wrong, nothing is moved. Deleting a note
removes the attachments that only it links in its level; ones another note links stay.

- A note that links more than 100 different attachments isn't moved to another level (`Can't move the attachments
  of <name>. Nothing was moved.`), and deleting it removes none of its attachments.
- An older kept version of a moved note still holds the old attachment names, so restoring it can link a missing or
  different file.

### Timed reminders

Give a todo a time to be reminded at: `context todo add 2026-11-16 14:00 Call Anna` (the time is `HH:mm`, or `H:mm`,
from `00:00` to `23:59`, right after the date). `context todos` then prints `2026-11-16 14:00` before the text, and
within one date the todos without a time come first, then the timed ones by time. In the desktop app the todo entry
row has a time field next to the date, and `/todo 2026-11-16 14:00 Call Anna` in the command palette does the same.
While the desktop app runs it checks every 30 seconds and shows a system notification (title `Context`, body
`14:00 Call Anna`) once for each open todo whose time has come; more than 3 at once are one `<n> todos are due.`
notification. A todo whose time passed while the app was closed is shown at the next start. The command line never
notifies. Each todo reminds once (`todos.json` notes that with a `notified` field); moving it with the day's end or
the next todo of a recurring one reminds again. On macOS the notification comes through `osascript`, on Windows
through PowerShell, so it may show `Windows PowerShell` as its source.

### Weekly review

`context review` prints one page for a weekly review: `Review, <from> to <to>` (the last 7 days, today included), then
`Changed notes` (when each note was last written, newest first; sensitive notes are left out), `Done todos` (done in
those 7 days), `Overdue` (open todos due before today) and `Next week` (the appointments from tomorrow to 7 days
ahead, one line per day an appointment touches). Every heading is printed, also when its section is empty. It never
opens `context.db`. In the desktop app, `/review` in the command palette shows the same page; click a note to open it.

### Note templates

Keep notes you write again and again (meetings, reviews) as plain Markdown files in the `templates` folder of the
data folder, for example `templates/meeting.md`. `context new Weekly sync --template meeting` creates the note
`Weekly sync` from it: every `{{date}}` becomes the date (`yyyy-MM-dd`), every `{{time}}` the time (`HH:mm`) and every
`{{title}}` the note's title; anything else stays as written, and no `# title` line is added. `context new <title>`
without `--template` creates a note with just its title. A template name is only matched against the files in
`templates/` (ignoring case), never used as a path; a name that isn't there prints the templates you have. In the
desktop app, `/new Weekly sync --template meeting` in the command palette does the same and opens the note.

### JSON output

Add `--json` to a read command (`version`, `todos`, `notes`, `note show`, `tags`,
`find`, `search`, `current`, `review`) to get one
JSON object on one line instead of text. Property names are fixed camelCase; missing values are `null`; dates are
`yyyy-MM-dd` and times ISO 8601 with offset. Errors stay one text line on stderr with the same exit codes, and
write commands and `help` refuse the flag.
Non-ASCII text and characters such as `+`, `&`, `'` and `<` are written as `\uXXXX` escapes, so the output is
plain ASCII; any JSON parser, `ConvertFrom-Json` included, reads them back.

| Command | Shape |
|---------|-------|
| `version` | `{"version":"1.2.3"}` |
| `todos` | `{"todos":[{"due":"2026-11-14","time":"14:00","text":"..."}]}` (`time` is `null` for a todo without one) |
| `notes` | `{"notes":[{"name":"A","locked":false}]}` |
| `note show <name>` | `{"name":"A","text":"..."}` |
| `tags` | `{"tags":[{"tag":"work","count":2}]}` |
| `tags <tag>` | `{"tag":"work","notes":["A"]}` |
| `find <key> <value>` | `{"key":"role","value":"Bereichsleiter","notes":["people/Anna Schmidt"]}` |
| `search <text>` | `{"query":"budget","results":[{"note":"A","line":3,"text":"Budget 2027"}]}` |
| `current` | `{"today":"...","appointments":[{"start":"...","end":"...","allDay":false,"title":"ACME kickoff","location":null,"related":[{"name":"projects/ACME Rollout","tags":["acme"],"words":["acme"]}]}],"calendar":{...},"due":[...],"linkedNotes":[...]}` |
| `review` | `{"from":"...","to":"...","changedNotes":[{"name":"A","modified":"..."}],"doneTodos":[{"due":"...","done":"...","text":"..."}],"overdue":[{"due":"...","text":"..."}],"appointments":[...],"calendar":{...}}` |

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

# Notes changed this week
(context review --json | ConvertFrom-Json).changedNotes | ForEach-Object { "$($_.modified) $($_.name)" }

# The text of one note
(context note show Plan --json | ConvertFrom-Json).text

# Everyone at ACME
(context find affiliation ACME --json | ConvertFrom-Json).notes
```

`current` writes its document even when part of the data can't be read: then it exits 1, its error lines are on
stderr, and `due` or `linkedNotes` is `null` (and each appointment's `related` when `context.db` can't be read).
`review` does the same: `doneTodos` and `overdue` are `null` when `todos.json` can't be read.

`related` lists up to three notes per appointment that share a tag or a title word with the appointment's title or
attendee names (meeting prep): `tags` are the matching tags (raw, without `#`), `words` the shared title words. Only
`current` has it; the appointments of `person` don't. In text mode `context current` prints each as
`related<TAB><name><TAB><matches>` right after its appointment line.
