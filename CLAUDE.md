# Context — conventions

Every factory agent reads this file before working on Context. Keep it short and accurate.

- **What it is:** a personal console app for keeping information in one place. The user stores
  notes as Markdown files in a `Context` folder inside their app data folder
  (`Environment.SpecialFolder.ApplicationData`). It shows the day's appointments from `.ics`
  files in `<root>/calendar/`; later it will pull in information from email and other sources.
- **Tags:** the user places tags in the Markdown files, and the tags link related files together.
  The links between files are stored in a database that lives in the same `Context` app data
  folder. The Markdown files stay the source of the content.
  - `TagParser.Parse` finds `#tag` (at line start or after whitespace, a letter first, then letters,
    digits, `-`, `_`; at most 50 chars; lower-cased, distinct) and `#[tag with spaces]` (`#[` at line start or
    after whitespace, up to the first `]` on the line, not followed by `(`; trimmed, whitespace runs become one
    space, then the same rules plus spaces) outside fenced code blocks and inline backtick spans. `#work` and
    `#[Work]` are one tag. `TagParser.Format` shows a tag as `#tag`, or `#[tag]` when it has a space; all tag
    output uses it. `TagIndex.NormalizeTag` (for `/tags <tag>`) accepts `work`, `#work`, `[a b]`, `#[a b]`. `TagIndex` keeps the index in SQLite at `<root>/context.db` (`Microsoft.Data.Sqlite`,
    `Pooling=False`, parameterized SQL only): `notes(name, modified)` (last-write UTC ticks, NULL for a
    note that couldn't be read so it is retried), `note_tags(note, tag)` and `note_links(note, target)`. `Sync(NoteStore)` updates it
    in one transaction and only re-parses files whose time changed. `PRAGMA user_version` holds the parser
    version (`TagIndex.ParserVersion`, now 2: note links); when it is lower, `Sync` clears every stored time in the same
    transaction (so all notes are re-parsed once) and then sets it. Bump it whenever parsing changes. Queries: `Tags()`, `NotesWithTag`,
    `FindNote`, `Links`. A database that can't be opened or isn't valid throws `TagIndexException` (an
    `IOException`) and is never deleted or overwritten. The file database waits up to `TagIndex.BusyTimeout` (5 s; an
    internal constructor argument shortens it for tests) for another process's lock, and `Sync` starts with
    `BEGIN IMMEDIATE`, so the desktop app and a CLI run can share it. `/tags`, `/tags <tag>` and `/links <note>`
    (`TagCommands`) call `Sync` first and show a red `Can't read context.db` hint on that error.
  - **Note links:** `NoteLinkParser.Parse` finds `[[Note name]]` anywhere in a line (`[[`, text with no `[` up to the
    first `]` on the line, then `]]`; trimmed; empty or over 103 chars (`MaxTargetLength`) is no link; distinct ignoring
    case, first spelling kept), skipping fences and code spans through `TagParser.LinesOutsideFences` and
    `TagParser.SkipCodeSpan` (shared, not copied). It scans each line once (cached next `]` and `[`). `[[...]]` is never
    a tag and `#[...]` never a link; `|alias` and `#heading` are part of the target. Targets are stored as written and
    resolved only at query time by `NameLookup.Resolve` (like `/open`: as written, then without one `.md`; exact, then
    ignoring case), never turned into a path, so a link to a missing note starts working once the note exists. `Links`
    merges tag links and note links into one `NoteLink` per other note (`Direction` `None`/`To`/`From`/`Both`,
    `Missing` = a `To` target that names no note, shown as written); never the note itself. `/links` adds a `Link`
    column (`links to`, `linked from`, `both ways`, `links to (missing)`, grey; missing names grey) only when some row
    has a note link, so tag-only output is unchanged.
- **UI:**
  - An input box at the bottom of the screen is used for navigation.
  - A todo list is always shown in a sidebar, sorted by due date. The user sets each todo's due date.
  - The `/current` view shows all relevant information for now, such as today's appointments, due
    todos and linked notes. `/current` (`CurrentView.Build` in Core reads the data, including the untruncated
    appointments, the time text and the calendar's `Zone`, in which the renderer shows the cache time; `CurrentCommands.Render` draws it; argument ignored) shows a steelblue1
    `Today, yyyy-MM-dd` title, a `Due` table of every todo with `Due <= today` (sidebar number, `yyyy-MM-dd`
    red overdue / steelblue1 today, one-line text) and a `Linked notes` table. A todo links to notes
    through tags: `TagParser.Parse` on the text of each due todo (at run time, not stored), then
    `TagIndex.NotesWithTag` after `Sync`; each note once, sorted by name ignoring case, with its matching
    tags. Grey empty-state lines when nothing is due or nothing links; a red hint for a bad `todos.json`
    (the title and appointments still show), and a red line in place of the linked notes for a bad `context.db`.
    Order: title, `Appointments`, `Due` (or its empty line), `Linked notes`.
  - **Calendar:** the user puts iCalendar files in `<root>/calendar/`. `CalendarStore(root, TimeZoneInfo)`
    reads only the top-level `*.ics` files (case-insensitive, no subfolders, never writes anything); a file over
    5 MB (`MaxFileBytes`) is not read. `Day(d)` returns a `CalendarDay`: the events whose local span overlaps
    `[d 00:00, d+1 00:00)` plus zero-length events starting on `d`, sorted all-day first, then start, then title
    (ordinal ignoring case), and the names of unreadable files (IO/access error, `FormatException`, too large).
    `IcsParser.Parse(text, zone)` scans linearly (no regexes): unfolds lines starting with space/tab, matches
    names ignoring case, unescapes `\n \N \, \; \\`, reads `SUMMARY`, `LOCATION`, `DTSTART`, `DTEND`, `STATUS` of
    each `VEVENT` (nested components like `VALARM` ignored). Times: `…Z` (UTC), `;TZID=` (unknown id = local),
    floating (local), `;VALUE=DATE` (all-day, `DTEND` exclusive). No `DTEND`: ends at start (all-day: one day).
    Skips `STATUS:CANCELLED`, missing/invalid `DTSTART`, end before start. No `BEGIN:VCALENDAR` throws
    `FormatException`. **Recurring events are not supported:** `RRULE`, `RDATE`, `EXDATE` are ignored, so a
    recurring event only shows on its first occurrence. `/current` shows an `Appointments` table (`Time`:
    `all day` or `HH:mm-HH:mm` in steelblue1, `…` for a side outside today, a midnight end shows as `00:00`;
    `Appointment`: one-line summary or `(no title)`; `Where`: grey one-line location), at most 15 rows (last
    `… N more`), or a grey `No appointments today. Put .ics files in <root>/calendar …` line, then a red
    `Can't read calendar/<file>` line per unreadable file (`calendar/` alone if the folder can't be listed).
  - **Exchange link:** `/calendar-link <url>` (`CalendarLinkView.Run` in Core acts and returns a kind plus the
    masked link only; `CalendarLinkCommands.Render` draws it) saves a published Outlook/Exchange ICS
    link; `/calendar-link` shows it masked with the last download time; `/calendar-link off` deletes the link
    and the cache. The link is a **bearer secret**: `CalendarLink(root)` keeps it in `<root>/calendar-link.txt`
    (one line, temp file + move, mode `0600` off Windows) and it is only ever shown as `CalendarLink.Mask`
    (`https://<host>/…`); never echo it, put it in another file or render an exception message that could hold
    it. `CalendarLink.Validate`: absolute `https` or `webcal` (stored as `https`), a host, no user-info (not even
    `@`), no whitespace. `CalendarFeed(root, link, HttpMessageHandler, utcNow, timeout?)` `Fetch()` never throws:
    `NotConfigured` (no request), the cache `<root>/calendar-link.ics` when younger than 5 min (no request), else
    one GET (10 s timeout, final URI must be `https`, 2xx, at most `CalendarStore.MaxFileBytes` by
    `Content-Length` and streamed bytes, must parse with `IcsParser`) that replaces the cache atomically and
    stamps its last-write time with `utcNow` (`Fresh`). Failures keep the cache untouched: `Stale` with it, or
    `Failed`; the reason is a fixed text (`timed out`, `network error`, `HTTP <code>`, `not HTTPS`, `over 5 MB`,
    `not a calendar`, `can't read calendar-link.txt`). `/current` merges the feed's events through
    `CalendarStore.Day(d, extra)` and adds, under the table, a grey `Exchange calendar offline, showing the copy
    from …` line (Stale) or a red `Can't download the Exchange calendar (<reason>).` line (Failed). Exchange
    feeds use `RRULE` a lot, so recurring meetings only show on their first occurrence. Tests use
    `FakeHttpHandler` and never touch the network; feed tests take their timeout from
    `CalendarFeedTests.TimeoutFor` (100 ms only with `FakeHttpHandler.Hang()`, 30 s for every handler that answers).
- **Data is plain:** every file in the data folder is stored as plain bytes; nothing is encrypted.
- **How it runs:** an interactive session that stays open until `/quit` (exit code 0). A Spectre
  `Layout` fills the terminal: the main view on the left, the `Todos` sidebar on the right (fixed
  32 columns), one hint row, then a 3-line bordered input panel at the bottom. After each command
  the screen is cleared and the whole layout redrawn. Input is read by our own `LineEditor`
  (`console.Input.ReadKey(intercept: true)` + `Cursor.SetPosition`); no `Live` display.
  - Commands start with `/`. Each feature registers its commands (name, description, handler) in the
    single `CommandRegistry` (lookup ignores case), and `/help` lists whatever is registered.
  - **Command popup:** typing `/` into an empty input opens a popup that lists
    `CommandRegistry.Match(line)` (Levenshtein-ranked, so typos still match): name in `steelblue1`,
    description in `grey`, one row each, escaped. It sits just above the input panel, ends on the hint
    row and grows upwards, full terminal width, at most 8 rows (`… N more` on the last one) and never
    over row 1. `LineEditor` redraws it in place after every key (`Cursor.SetPosition`, no clear and no
    layout redraw); rows it stops covering are restored from the last frame, which `App.Draw` keeps
    as `Segment.SplitLines` (`App.RestoreRow`). Enter, Esc, or a line that no longer starts with `/`
    closes it. Esc means two things: with the popup open it closes it and keeps the text (it stays
    closed until the line stops starting with `/`); with no popup it clears the line.
  - Unknown commands and other errors show a short `[red]` hint on the row just above the input
    panel, never a stack trace, and never end the loop. The next command clears the hint.
  - **Notes:** `NoteStore` keeps each note as a top-level `<Title>.md` file in its root folder
    (`NoteStore.DefaultFolder()` = `<ApplicationData>/Context`; the folder is created on the first
    `/new`). A new note's content is `# <Title>\n` (UTF-8, no BOM), written with `FileMode.CreateNew`.
    Titles are validated (`NoteStore.ValidateTitle`: no `/ \ :`, invalid file-name or control chars,
    no leading `.`, at most 100 chars, no Windows reserved name (CON, PRN, AUX, NUL, COM1-9, LPT1-9, also before a dot), no trailing `.` or space) after `NoteStore.NormalizeTitle` trims them and strips any trailing
    `.md` (ignoring case), so `/new x.md` creates `x.md`; names in `/open` are only matched against `List()` (as typed first, then with
    one `.md` stripped), never turned into a path. Note text is shown as plain `Text`, never parsed as markup.
    `/open` on an unreadable note shows a red `Can't read <name>.` hint.
    `/export <note> <path>` (note = the longest leading part that names a note; `~` = home) writes the note's plain
    text (UTF-8, no BOM, `FileMode.CreateNew`) outside the root (`NoteStore.Contains` resolves links and ignores
    case). `/import <path>` reads a
    file outside the root (at most 5 MB, never changed) and stores it as the note
    `NormalizeTitle(file name)` via `NoteStore.Save` (create, or replace an existing note through temp + move);
    the hint says to delete the source.
  - **Todos:** `TodoStore` keeps the list in `<root>/todos.json` (same root as notes; System.Text.Json,
    camelCase, UTF-8 no BOM). `List()` sorts by `Due`, then `Created` (UTC), then file order. Writes go to
    a temp file in the root, then `File.Move(..., overwrite: true)`; a file that can't be read or parsed
    throws `TodoStoreException` and is never overwritten. Each `TodoItem` has a `Guid Id` (stored as `id`, never
    shown to the user; new ones from `Guid.NewGuid` or the injected `newId`). A todo stored without an id (files from
    before ids), or with an empty or repeated one, gets `TodoStore.DerivedId` (SHA-256 of created, due, text and its
    rank among identical todos), so reads never write and the id stays the same until the next write stores it; an
    `id` that is not a GUID makes the file unreadable. `Complete(Guid id)` removes that todo in one read and write
    and returns it, or returns `null` and writes nothing. `/todo <yyyy-MM-dd|today|tomorrow> <text>` adds
    (text at most 200 chars, past dates allowed); `/done <n>` picks the n-th todo as numbered in the console
    sidebar and removes it by its id. `TodoSidebar.Render(rows, width)` draws `<n> <MM-dd> <text>` lines (date `red` overdue,
    `steelblue1` today, `grey` later; text cleaned, escaped and cut to one line with `…`), `… N more` in
    grey on the last row when they don't fit, `No todos yet` when empty and a red `Can't read todos.json`
    line when the file is bad. `App` takes an optional `TodoSidebar`; without one it shows `No todos yet`.
    `today` is injected as `Func<DateOnly>` (and `TodoStore` takes an optional UTC clock), so tests never
    use the real clock.
  - Tests drive the loop by queuing input on `TestConsole.Input` (`PushTextWithEnter`, `PushKey`)
    and always end with `/quit`, so they can't hang.
- **Command line (scriptable):** `Context <command> [args]` (any arguments) runs one command through `Cli`
  (`src/Context/Cli.cs`) and exits; no arguments starts the interactive console as before. `Cli(stdout, stderr,
  folder, today, now, HttpMessageHandler)` (it takes no stdin and no environment, so no command can read either) writes plain text only (results on stdout with `\n` line ends, tab-separated
  columns; errors on stderr), no markup, no layout, popup, line editor or prompt; `int Run(args)` never throws.
  Commands (matched ignoring case): `help`/`--help`/`-h` (never reads the folder), `version`/`--version` (prints
  `Context <version>` from `AppVersion.Current`: the assembly's informational version without `+...`; never reads the
  folder), `capture <text...>`
  (`Inbox.Capture`: controls to spaces, trimmed, 1-1000 chars, appends `- yyyy-MM-dd HH:mm <text>` to `inbox.md` via
  `ReadForEdit` + `SaveIfUnchanged`, retries 3 times on a change on disk; a missing inbox is created as `# inbox\n` +
  the line through `NoteStore.CreateWithText`, temp file + no-overwrite move), `todos` (`<n>\t<date>\t<text>`),
  `todo add <date> <text...>` (`TodoInput` rules, shared with `/todo`), `todo done <n>`, `notes`, `note show <name...>`,
  `tags [tag...]` (`<tag>\t<count>` or note names), `links <note...>` (`<name>\t<link>\t<tags>`), `current` (the
  `/current` sections as text, no row caps; the console's red lines go to stderr and make the exit code 1),
  `import <path>`, `export <note> <path>` (paths relative to the current directory, no `~`). Exit codes: 0 success,
  1 error (one stderr line, nothing on stdout; `current` may write both), 2 usage error (one stderr line ending in
  `Run 'context help'.`; unknown commands never touch the folder). All text from disk goes through `SafeText.Clean`
  (one-line fields also `\n` to space). Tests (`CliTests`) use the
  internal constructor with a fixed zone.
- **Stack:** .NET 10 (`net10.0`), C# with nullable reference types and implicit usings on.
  `Spectre.Console` 0.57.2 handles all terminal output (app only). `Microsoft.Data.Sqlite` 10.0.14 for `context.db`
  (referenced by `Context.Core` only; it reaches the app through the project reference). Tests: xUnit + `Spectre.Console.Testing` 0.57.2.
  Desktop: `Photino.Blazor` 4.0.13 (Apache-2.0; Blazor in the OS web view: WebView2 / WKWebView), `Context.Desktop` only; its tests
  use `bunit` 2.13.3 and `Jint` 4.17.0 (BSD-2, test project only; runs the editor bundle).
  `Velopack` 1.2.161 (MIT; the Windows installer and updater), `Context.Desktop` only; its version must equal the
  `vpk` tool version in `release.yml` (`ReleaseWorkflowTests` compares them), so change both together.
  Editor bundle: `src/Context.Desktop/editor/` is an npm project (exact versions in `package.json`, `package-lock.json`;
  ProseMirror + prosemirror-markdown/markdown-it, esbuild; licenses in `THIRD-PARTY-LICENSES.txt`, update it when a
  package changes). After changing `editor/src/*`, run `cd src/Context.Desktop/editor && npm install && npm run build`
  (Node from `brew install node`) and commit the rebuilt `wwwroot/editor/editor.js` and `editor.css`; `dotnet build`
  and `dotnet test` never need Node. `node_modules` is ignored and `editor/**` is excluded from the C# project.
- **Build:** `dotnet build` (must stay at 0 warnings)
- **Test command:** `dotnet test`
- **CI:** `.github/workflows/ci.yml` (GitHub Actions, `ubuntu-latest`) runs on every push to `develop` and `main`
  and on every pull request: `dotnet restore`, `dotnet build --no-restore -warnaserror`, `dotnet test --no-build`, at the repo root.
  Read-only token, no secrets, actions pinned to commit SHAs; `CiWorkflowTests.cs` checks the file as text.
- **Release:** `.github/workflows/release.yml` (next to `ci.yml` there is no third workflow; `contents: write`, no secrets)
  has three triggers: a push to `develop`, a merged `develop` -> `main` pull request titled exactly `release vX.Y.Z`,
  and a pushed `vX.Y.Z` tag. Three jobs: `release` (`ubuntu-latest`), then `desktop-macos` (`macos-latest`) and
  `desktop-windows` (`windows-latest`, `shell: bash`), which both `needs: release`. Steps of `release`: check out,
  set up .NET, Version, `dotnet test -c Release`,
  `dotnet pack src/Context -c Release -p:Version="$VERSION"`, smoke check, create the GitHub release (notes start
  with `.github/release-notes-header.md`: how to open an unsigned build), prune.
  - **Versions:** a `develop` push makes a prerelease `X.Y.(Z+1)-develop.<run number>`, one patch above the highest
    release tag (`.github/scripts/develop-version.sh`; `0.0.1-develop.<n>` while there is no tag). A push that changes
    only the root `backlog.md` is skipped (`paths-ignore`) and makes no prerelease. A release PR takes its version from
    the title (`.github/scripts/release-pr-version.sh`: a plain `X.Y.Z`, no prerelease label, tag not there yet); the release and its tag are
    made on the merge commit, and closed-unmerged PRs, PRs from other branches and fork PRs never reach a step. A pushed
    tag gives its own version, must be on `main`, and a `-develop` tag is refused. The version is checked against SemVer
    in the Version step and again by the `ValidateVersion` target in `Context.csproj` (`<Version>0.1.0</Version>` there
    is only the local default).
  - **What ships:** on every release, three things. (1) The console app as the .NET tool package
    `Context.<version>.nupkg` (`PackAsTool`, command `context`, written to the ignored `nupkg/` folder). The smoke
    check installs it from a throwaway nuget.config that knows only the local folder, requires
    `context --version` to print `Context <version>`, and runs `context tags` on a fresh data folder so a missing
    SQLite native library fails the release. Nothing goes to nuget.org. (2) The desktop app for macOS:
    `Context-<version>-osx-arm64.zip` and `Context-<version>-osx-x64.zip`, each a `Context.app` built by
    `.github/scripts/macos-app.sh <publish-dir> <version> <out-dir>` (Info.plist + all published files in
    `Contents/MacOS/`; it neither signs nor zips), ad-hoc signed (`codesign -s -`, the only identity) and zipped
    with `ditto`. (3) The desktop app for Windows, packed by `vpk` (`--packId ContextDesktop`, `--mainExe
    Context.Desktop.exe`): `Setup.exe`, the portable zip, this version's full `.nupkg`, a delta `.nupkg` when
    `vpk download github` found an earlier release of the channel (a failed download only warns), and the feed
    files (`releases.<channel>.json`, `assets.<channel>.json`, `RELEASES*`); never a package of an older version.
    The desktop jobs get version, tag and commit from the `release` job's outputs (job-level `env:` `VERSION`,
    `TAG`; checkout `ref`), re-check the version against SemVer before any `dotnet` call, and upload with
    `gh release upload "$TAG" ... --clobber`, so re-running a failed desktop job works. A failed desktop job leaves
    a release with fewer assets.
  - **Channels and updater:** two channels. A prerelease version is packed with `--channel develop`, a proper
    release with vpk's default channel (`win`). A copy installed by `Setup.exe` updates itself at start-up
    (`StartupUpdate`, see the desktop layout) from the channel it was installed from; the portable copy and the
    macOS zips never update. **Trust model:** nothing is signed (no certificate, no notarization, no signature check
    in the updater; signed builds are backlog #15). An update is trusted only as an HTTPS download from this
    repository's GitHub releases, which only the release workflow writes, so whoever can write a release here can run
    code on every installed copy. No token is embedded in the app; while the repository is private the
    unauthenticated updater finds nothing and stays silent. The installer cannot run in `dotnet test`: after a change
    to the desktop jobs, the next develop run is the real check.
  - **Pruning:** after a proper release `vX.Y.Z` the `Prune old prereleases` step (`continue-on-error`) deletes the
    `-develop` prereleases with core <= X.Y.Z and their tags (`.github/scripts/old-prereleases.sh`); proper releases
    and higher prereleases stay.
  - **Rules:** actions pinned to commit SHAs (the same two as `ci.yml`, tag in the trailing comment); no `${{ }}`
    inside a `run:` script: PR fields, refs and the token reach the shell only through `env:` and the runner's own
    variables. `github.token` is the only credential (the two release steps, the Windows `vpk download` and the two uploads). The three version scripts are Times' (`release-pr-version.sh` differs in one pattern: it refuses every prerelease label, Times' only `-develop`). `ReleaseWorkflowTests.cs` reads `release.yml`
    only through the small YAML reader `tests/Context.Tests/WorkflowYaml.cs` (block mappings and sequences, plain and
    quoted scalars, `|` and `>` blocks, comments; anything else throws), so reformatting the YAML does not break the
    tests, and it runs the scripts with `bash` (also the Windows `Pack` and `Upload` steps, against stub `vpk` and `gh` commands). `/factory release --project Context` opens the release PR.
- **Run:** `dotnet run --project src/Context` (console), `dotnet run --project src/Context.Desktop` (desktop window)
- **Layout:**
  - `Context.slnx`: the solution. `Directory.Build.props` turns on `EnforceCodeStyleInBuild`.
  - `src/Context.Core/`: class library with the data and storage code, no terminal UI. **It must never
    reference `Spectre.Console`** (no file there mentions `Spectre`); its only package is `Microsoft.Data.Sqlite`.
    Types stay `internal` in `namespace Context;`; `InternalsVisibleTo` grants `Context`, `Context.Tests`,
    `Context.Desktop` and `Context.Desktop.Tests`.
    It holds the note files in `NoteStore.cs`, the todo list in `TodoStore.cs`, tag parsing in `TagParser.cs`,
    `[[note link]]` parsing in `NoteLinkParser.cs`, the SQLite tag and link index (with `NoteLink`, `NameLookup`) in `TagIndex.cs`, the `.ics` parser and `Appointment` in `IcsParser.cs`, the calendar
    folder reader and `CalendarDay` in `CalendarStore.cs`, the secret link file in `CalendarLink.cs`, the
    download and cache in `CalendarFeed.cs`, the display sanitizer in `SafeText.cs`, bounded file reads (`BoundedFile.cs`, managed code only: a file that reports length 0 is returned empty without being opened, so a FIFO named `x.md` never blocks; an opened stream that can't seek is refused; Core has no P/Invoke, checked by `CoreDependencyTests`) and the view data steps
    `CurrentView.cs` (`/current`), the inbox capture in `Inbox.cs`, the todo input rules (`TryParseDate`,
    `MaxTextLength`) in `TodoInput.cs`, `TagsView.cs` (`/tags`), `LinksView.cs` (`/links`) and `CalendarLinkView.cs`
    (`/calendar-link`); `/notes` uses `NoteStore.List()` directly.
  - **Data step in Core, Spectre renderer in the app:** a view command's handler calls a Core data step that
    returns plain types (a kind plus raw text, never markup, never cleaned or escaped, no exception messages), then
    an `internal static Render(...)` in its `*Commands` class that only maps that view to a `CommandResult` (no
    store, index, feed or file access). Markup, colours, `SafeText.Clean` + `Markup.Escape`, fixed message texts
    and row caps live in the renderer, so any other front end can draw the same data its own way.
    View records (and `DueTodo`, `LinkedNote`) override `ToString()` to return only their type name, so their raw text never
    leaks through formatting; nobody formats a view directly.
  - `src/Context/`: the console app (references `Context.Core` by `ProjectReference`, plus `Spectre.Console`).
    `Program.cs` only wires things up (with arguments it sets UTF-8 output and returns `new Cli(...).Run(args)` over
    `Console.Out`/`Error`; with none it builds the registry with
    `CreateDefault()` + `NoteCommands.Register` + `TagCommands.Register` + `TodoCommands.Register` + `CalendarLinkCommands.Register` + `CurrentCommands.Register` (with a `CalendarStore(folder, TimeZoneInfo.Local)` and a `CalendarFeed` over a `SocketsHttpHandler` with `MaxAutomaticRedirections = 5`); passes the registry, a `TodoSidebar`
    and `AnsiConsole.Console` to `App`, returns `App.Run()`); the loop, layout and dispatch live in `App.cs`, commands in `CommandRegistry.cs`,
    input in `LineEditor.cs`, the command popup's rows and `PopupArea` in `CommandPopup.cs`,
    `/new`, `/notes`, `/open`, `/export`, `/import` in `NoteCommands.cs`, `/todo` and `/done` in `TodoCommands.cs`, the sidebar body
    in `TodoSidebar.cs`, `/tags`, `/links` in `TagCommands.cs`, `/current` in `CurrentCommands.cs`, and
    `/calendar-link` in `CalendarLinkCommands.cs`, the scriptable command line in `Cli.cs`, and the version text in `AppVersion.cs`. Code that needs Spectre stays here; data and storage go to Core.
  - `src/Context.Desktop/`: the desktop app (`Microsoft.NET.Sdk.Razor`, `WinExe`, references `Context.Core`
    only, never Spectre or the console app; components and classes in `namespace Context`). `Program.cs` only wires
    things up (stores built like the console's, Photino window `Context` 1100x750, dev tools
    only in Debug). `DesktopState` builds a `DesktopSnapshot` (`Failed`, `Today`, `CurrentView? Current`,
    `Todos`, `Notes`, unreadable flags, `Folder`) off the UI thread with `Refresh()` (one at a time; requests during a run
    coalesce into one more run) and raises `Changed`; `Tick()` refreshes when the day changed or 5 min passed.
    `DataWatcher` (one `FileSystemWatcher` with subfolders, `IsRelevant` filter, 300 ms debounce, 30 s tick timer;
    never creates the folder) calls them. Components (`App`, `Main`, `CurrentPanel`, `TodoPanel`, `NotesPanel`)
    inherit `StateComponent`, which injects the state and `DesktopActions` (internal types can't be
    public parameters). `DesktopActions` (with `ActionResult`: `ActionKind` `Done`/`Refused`/`Failed`, `ActionReason`,
    raw text) is the only write path: it never throws, runs one action at a time (`Busy`), refreshes the state after a change and has the test
    hook `BeforeWrite`. `CompleteTodo(todo, confirmation)` removes the todo with `todo.Id` through
    `TodoStore.Complete(id)` (a todo no longer stored: `The todo list changed. Nothing was done.`). `ConfirmDialog` (in-window
    `role="dialog"` overlay, focus on `Cancel`, Esc cancels, `Busy` disables it) is the only creator of a
    `Confirmation` (public sealed, internal constructor), which destructive actions require. The desktop shows no todo
    numbers (not in `TodoPanel`, not in the `Due` table): `TodoPanel` shows each todo as a `<label>` line with a
    checkbox, date and text; ticking it opens the dialog (the box stays ticked while it is open), and a
    `role="status"` result line shows `Done: <text>` (accent) or the fixed red text.
    **Editing:** clicking a note name (`NotesPanel` button), a link or `/open` in the palette shows `NoteEditor` in the
    main region (`Main` keeps the open name; a dirty editor asks `Discard changes to <name>?` first). `NoteEditor`
    reads through `DesktopState.ReadForEdit` (`NoteStore.ReadForEdit` -> `EditableNote`: text with `\n`, raw-byte
    SHA-256 `Version`, CRLF and BOM flags), mounts the page editor through `EditorInterop` (the only class that touches
    `IJSRuntime`; `[JSInvokable]` `OnTag`/`OnLink` only navigate) and saves on the Save button or Ctrl/Cmd+S through
    `DesktopActions.SaveNote` -> `NoteStore.SaveIfUnchanged` (unchanged text writes nothing; version re-checked before
    the move; CRLF and BOM kept; temp file mode `0600`). Open and save both stop at `NoteStore.MaxEditBytes` (5 MB of plain
    file bytes, read through `BoundedFile`): a bigger or non-seekable note can't be opened (`Can't read <name>.`), a save
    that would be bigger is refused, and a file that grew past it counts as changed on disk. New components are styled
    in `app.css`; `editor.css` holds only `.note-editor-body` and ProseMirror's own classes. `TagPanel` lists `DesktopState.NotesWithTag`. `CommandPalette` (Ctrl/Cmd+K) ranks
    `/current`, `/open`, `/save`, `/tags`, `/todo` with `CommandMatcher` (shared with `CommandRegistry.Match`).
    `TodoPanel` has an entry row (`DesktopActions.AddTodo`). The page editor is `wwwroot/editor/editor.js`
    (`globalThis.ContextEditor`), built from `editor/src/` (`schema.js`, `markdown.js` with markdown-it `html: false`,
    `view.js`, `index.js`); `isLossless` false shows the grey `Saving will reformat parts of this note.`
    **Update:** `Program.Main` starts with `VelopackApp.Build().Run()` (Velopack's install/update hooks), and before
    the window opens `StartupUpdate(IUpdateSource).Start()` runs the silent update on a pool thread: it returns at once
    and never throws. Not `IsInstalled`: nothing is called. Otherwise `CheckAsync`; when a newer version is found,
    `DownloadAsync` once, then `ApplyOnExit` once (put in place after the app closes, no restart). Any exception or
    cancellation is swallowed with no output; the next start tries again. `StartupUpdate.IncludePrereleases(version)`
    is true only for a SemVer version with a prerelease label (`StartupUpdate.CurrentVersion` is the assembly's
    informational version). `VelopackUpdateSource` is the one real source: Velopack's `UpdateManager` over
    `GithubSource` with the fixed repository URL, no access token, no explicit channel and no downgrade; installed
    means Windows, installed by `Setup.exe` and not the portable copy. There is no update UI.
    `wwwroot/index.html` (CSP meta tag, only `_framework/blazor.webview.js` and `editor/editor.js`), `wwwroot/app.css`
    and `wwwroot/editor/editor.css`.
  - **Desktop rules:** the Core writing calls (`Create`, `Save`, `SaveIfUnchanged`, `Add`, `Complete`, `Set`, `Clear`, `Delete`,
    `ClearCache`) appear only in `DesktopActions.cs` (`DotNetObjectReference.Create(` once in `EditorInterop.cs` is the one exemption; the reads still write
    only through `TagIndex.Sync` and `CalendarFeed.Fetch`, as in the console). `DesktopActions` is named only by
    `.razor` files, `Program.cs` and `StateComponent`, never by `DesktopState`, `DataWatcher` or `DesktopSnapshot`, so
    nothing writes on render, refresh, the watcher or the tick. In `.razor` files every `@code` method that calls
    `Actions.` is bound by an event attribute in the same file (`@on<event>=` on an element or an `On<Event>=`
    callback parameter on a component), is no lifecycle method (`OnInitialized*`, `OnParametersSet*`,
    `OnAfterRender*`, `SetParametersAsync`, `BuildRenderTree`) and is called by no other method; no `.cs` file calls
    `Actions.`. Destructive actions take a `Confirmation`, and `new Confirmation(` appears only in
    `ConfirmDialog.razor`.
    `Velopack` is named only in `VelopackUpdateSource.cs` and `Program.cs`; the update classes (`IUpdateSource.cs`,
    `StartupUpdate.cs`, `VelopackUpdateSource.cs`) never mention `NoteStore`, `DefaultFolder`, `DesktopActions`,
    `Environment.GetEnvironmentVariable`, `args` or `Console.`, so an update never touches the data folder; no test
    references `VelopackUpdateSource` (tests use `FakeUpdateSource`, never the network).
    Outside text goes through `DesktopText.OneLine` and Blazor's default encoding: never `MarkupString`,
    `AddMarkupContent` or `@((` raw markup; no `style=`, `<a `, `<form` or `<script` in `.razor` files; every
    `<button` has `type="button"`; no plain HTML event attributes (`on<event>=` without `@`); only `EditorInterop.cs`
    mentions `IJSRuntime`/`JSRuntime`/`JSInvokable`, and its `[JSInvokable]` methods only raise events (never `Actions.`).
    The editor sources (`editor/src/*.js`) use no `innerHTML`, `outerHTML`, `insertAdjacentHTML`, `document.write`,
    `eval` or `new Function`. Every `<input>` is `type="checkbox"`, `"text"`, `"date"` or `"search"`, without `@bind`,
    and there is no `<textarea>`. No CDN or external fonts; error
    lines are fixed texts, never exception messages; the calendar link stays inside `CalendarFeed`. Texts match the
    console renderer's. Colours by CSS class (`accent`, `grey`, `red`, dates `overdue`/`today`/`later`); long lines
    cut by CSS with the full text in `title`.
  - `tests/Context.Tests/`: references both projects; one test class per production class (Core and app alike) (`AppTests.cs` tests `App.cs`),
    `CiWorkflowTests.cs` and `ReleaseWorkflowTests.cs` (the workflows and their three jobs, the release scripts and the tool packaging; with `WorkflowYaml.cs` and its `WorkflowYamlTests.cs`), `ViewParityTests.cs` (golden ANSI output of `/current`, `/notes`, `/tags`, `/links`, `/calendar-link`; change
    its expected strings only when a spec changes that output), `CoreDependencyTests.cs` (architecture test: fails on any
    Spectre assembly, package, project reference or source mention in `Context.Core`), plus shared helpers such as `AnsiWrites.cs` (splits ANSI output on cursor moves),
    `ConsoleSession.cs` (everything wired like `Program.cs`), `RemovedFeature.cs` (the names of the removed
    whole-folder protection, spelled in pieces; `RemovedFeatureSourceTests.cs` fails when `src/`, `tests/` or this file
    names that feature again) and `FakeHttpHandler.cs` (canned HTTP responses). Note, todo, tag and calendar tests use their
    own temp folder (deleted in `Dispose`), never the real app data folder.
  - `tests/Context.Desktop.Tests/`: xUnit + bunit, references `Context.Desktop` and `Context.Core`; one test class per
    component or class, plus `EditorBundleTests` (Jint on the committed `editor.js` against `RoundTrip/lossless` and
    `RoundTrip/lossy` fixtures, compared with the C# parsers; `.gitattributes` keeps their bytes), `EditorJs` (bunit
    JSInterop setups for the page editor), `EditingFlowTests` and `TodoEntryTests`, `StartupUpdateTests` (with `FakeUpdateSource`), `DesktopDependencyTests` (no Spectre, no Jint
    outside tests, exact npm versions, Velopack only in `Context.Desktop`), `DesktopSourceTests` (raw markup, risky elements,
    CSP, write sites, event-handler-only actions, button types, checkbox-only inputs, JS interop), `DesktopFixture` (temp folder, injected
    day/clocks, `FakeHttpHandler` linked from `Context.Tests`, a `BunitContext` serving the state and the actions),
    `FileSnapshot` (bytes and last-write times of a folder, to prove nothing changed) and `RepoFiles`.
- **Style:**
  - Write all output through an injected `IAnsiConsole`, never `Console.*` or the static
    `AnsiConsole.*` methods (except in `Program.cs`). Tests use `TestConsole` and assert on `.Output`.
    The one exception is `Cli`, which writes plain text through injected `TextWriter`s (stdout, stderr) and mentions
    no `Spectre` and no `Console.` (checked by `CliTests`).
  - Text from disk or other outside sources (note text and names, OS exception messages, later email
    and calendar data) goes through `SafeText.Clean` before rendering: it keeps `\n`, turns `\r\n`
    into `\n` and `\t` into 4 spaces, and replaces every other C0/DEL/C1 control with `�` (U+FFFD).
    Then wrap it in `Markup.Escape` if it goes into markup. Never pass such text to the terminal raw.
  - Wrap any user or external text in `Markup.Escape` before it goes into markup.
  - Use Spectre widgets (`Table`, `Panel`, `Rule`, `Layout`) instead of hand-drawn ASCII.
    Colours: `SteelBlue1` accents, `grey` secondary text, `red` errors.
  - File-scoped namespaces, `internal sealed` unless a type must be public.
  - No primary constructors anywhere (classes, structs or positional records). Write a
    conventional constructor that assigns to fields or properties.
  - Private fields are camelCase **without** a leading underscore (`console`, not `_console`).
    `this.` only where a field and a constructor parameter share a name.
  - No `var`: always write the explicit type. Braces on every `if`/`else`/`for`/`foreach`/`while`
    body, even single lines.
  - `.editorconfig` holds these rules. The build enforces it (`EnforceCodeStyleInBuild`), so a
    style break is a build warning, and builds must stay at 0 warnings.
  - `Run` returns a process exit code: 0 for success, non-zero for errors.
  - No new NuGet packages without a reason in the spec. The approver escalates any new dependency.
- **Git:** `develop` is the working branch: factory work branches off it as `factory/<job-id>` and is merged back with `--no-ff`. `main` holds releases only and is pushed to only for a new release.
  `.gitignore` is the standard `dotnet new gitignore`.
