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

### JSON output

Add `--json` to a read command (`version`, `todos`, `notes`, `note show`, `tags`, `links`, `current`) to get one
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
| `current` | `{"today":"...","appointments":[...],"calendar":{...},"due":[...],"linkedNotes":[...]}` |

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

# The text of one note
(context note show Plan --json | ConvertFrom-Json).text
```

`current` writes its document even when part of the data can't be read: then it exits 1, its error lines are on
stderr, and `due` or `linkedNotes` is `null`.
