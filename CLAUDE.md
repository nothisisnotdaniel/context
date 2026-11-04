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
    `IOException`) and is never deleted or overwritten. `/tags`, `/tags <tag>` and `/links <note>`
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
    `FakeHttpHandler` and never touch the network.
- **Encryption at rest (optional, off by default):** `<root>/encryption.json` (`Vault.FileName`) means "on". It holds
  the version, KDF (`PBKDF2-HMAC-SHA256`, `Vault.DefaultIterations` = 600,000, 16-byte salt) and a random 32-byte data
  key wrapped with AES-256-GCM; nothing secret in plain form. Passphrases are normalized to Unicode form C
  (`Vault.NormalizePassphrase`) before PBKDF2, so the same text from another OS unlocks. `Unlock` refuses a key file
  with fewer than `Vault.MinUnlockIterations` (100,000) iterations with a `VaultException` (tests pass a lower minimum to
  `new Encryption(root, minIterations)` / the internal `Vault.Unlock(root, pass, min)`). `Vault` (`Create`, `Unlock` (throws
  `WrongPassphraseException`), `Encrypt`/`Decrypt(kind, bytes)`, `IsEncrypted`, `Dispose` zeroes the key; tests use the
  internal `Create(root, pass, iterations)` with 1,000). Encrypted file: `00 43 54 58 45` + version `01` + 12-byte random
  nonce + ciphertext + 16-byte tag; AAD = kind (`note`, `todos`, `calendar-link`, `calendar-cache`, `calendar`). A
  failed decrypt throws `VaultException` (an `IOException`, fixed message), so stores treat it like any unreadable file.
  `Encryption` is the shared session holder (`Vault?`), passed as an optional last constructor argument to `NoteStore`,
  `TodoStore`, `TagIndex`, `CalendarStore`, `CalendarLink`, `CalendarFeed`; `null` or no vault = plain, byte-for-byte as
  before. Reads detect the magic (plain files are still read; encrypted ones need the vault); writes (and their temp
  files) are ciphertext while the vault is set. With the vault set `TagIndex` uses an in-memory SQLite database
  (`PRAGMA temp_store=MEMORY`, rebuilt each session) and never opens `context.db`. `Encryption.Migrate()` /
  `DecryptAll()` convert the data set (top-level `*.md`, `todos.json`, `calendar-link.txt`, `calendar-link.ics`,
  top-level `calendar/*.ics`) file by file: temp file (mode `0600`), read back and verify, keep the last-write time,
  then move; failures stay as they were and are returned by name (`BeforeReplace` is the test fault hook). `Migrate`
  then deletes `context.db*`; `DecryptAll` deletes `encryption.json` last and turns the session off only when every
  file worked. `/encrypt` (`EncryptionCommands`): off → new passphrase twice (masked, at least 12 chars, says a lost
  passphrase loses the data), `Vault.Create`, `Migrate`; on → `Migrate` only, no passphrase; refused while
  `Encryption.EncryptedFilesWithoutKey()` > 0 (encrypted files but no `encryption.json`). `/decrypt` asks the
  passphrase again (`Encryption.CheckPassphrase`), then `DecryptAll`. Esc cancels both with no change. At
  start-up `App` (given the `Encryption`) asks `Passphrase:` in the input panel (sidebar empty while locked; the hint
  says Esc quits); Esc returns 0, a wrong one shows red `Wrong passphrase.`, 3 wrong ones (or a bad or weak
  `encryption.json`, shown by its fixed message, or the input ending) return 1. Without `encryption.json` but with
  encrypted data files, the first frame shows a red `N encrypted files but no encryption.json: new data is NOT
  encrypted.` hint.
  Passphrases are read by `PassphrasePrompt` (label in the input panel, hint on the hint row) over
  `LineEditor.ReadSecret` (shows `•` per character, no popup); they are never shown, stored or put in any text.
  The OS key store, changing the passphrase and an encrypted on-disk tag index are not supported.
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
    `/open` on an unreadable or undecryptable note shows a red `Can't read <name>.` hint.
    `/export <note> <path>` (note = the longest leading part that names a note; `~` = home) writes the note's plain
    text (UTF-8, no BOM, `FileMode.CreateNew`) outside the root (`NoteStore.Contains` resolves links and ignores
    case); in encrypted mode the hint starts with a red `This copy is not encrypted.` `/import <path>` reads a plain
    file outside the root (at most 5 MB, not encrypted, never changed) and stores it as the note
    `NormalizeTitle(file name)` via `NoteStore.Save` (create, or replace an existing note through temp + move;
    encrypted when on); the hint says to delete the source.
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
- **Stack:** .NET 10 (`net10.0`), C# with nullable reference types and implicit usings on.
  `Spectre.Console` 0.57.2 handles all terminal output (app only). `Microsoft.Data.Sqlite` 10.0.14 for `context.db`
  (referenced by `Context.Core` only; it reaches the app through the project reference). Tests: xUnit + `Spectre.Console.Testing` 0.57.2.
  Desktop: `Photino.Blazor` 4.0.13 (Apache-2.0; Blazor in the OS web view: WebView2 / WKWebView), `Context.Desktop` only; its tests
  use `bunit` 2.13.3 and `Jint` 4.17.0 (BSD-2, test project only; runs the editor bundle).
  Editor bundle: `src/Context.Desktop/editor/` is an npm project (exact versions in `package.json`, `package-lock.json`;
  ProseMirror + prosemirror-markdown/markdown-it, esbuild; licenses in `THIRD-PARTY-LICENSES.txt`, update it when a
  package changes). After changing `editor/src/*`, run `cd src/Context.Desktop/editor && npm install && npm run build`
  (Node from `brew install node`) and commit the rebuilt `wwwroot/editor/editor.js` and `editor.css`; `dotnet build`
  and `dotnet test` never need Node. `node_modules` is ignored and `editor/**` is excluded from the C# project.
- **Build:** `dotnet build` (must stay at 0 warnings)
- **Test command:** `dotnet test`
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
    download and cache in `CalendarFeed.cs`, the key file and file format in `Vault.cs`, the session holder and
    migration in `Encryption.cs`, the display sanitizer in `SafeText.cs`, and the view data steps
    `CurrentView.cs` (`/current`), `TagsView.cs` (`/tags`), `LinksView.cs` (`/links`) and `CalendarLinkView.cs`
    (`/calendar-link`); `/notes` uses `NoteStore.List()` directly.
  - **Data step in Core, Spectre renderer in the app:** a view command's handler calls a Core data step that
    returns plain types (a kind plus raw text, never markup, never cleaned or escaped, no exception messages), then
    an `internal static Render(...)` in its `*Commands` class that only maps that view to a `CommandResult` (no
    store, index, feed or file access). Markup, colours, `SafeText.Clean` + `Markup.Escape`, fixed message texts
    and row caps live in the renderer, so any other front end can draw the same data its own way.
    View records (and `DueTodo`, `LinkedNote`) override `ToString()` to return only their type name, so their raw text never
    leaks through formatting; nobody formats a view directly.
  - `src/Context/`: the console app (references `Context.Core` by `ProjectReference`, plus `Spectre.Console`).
    `Program.cs` only wires things up (builds the registry with
    `CreateDefault()` + `NoteCommands.Register` + `TagCommands.Register` + `TodoCommands.Register` + `CalendarLinkCommands.Register` + `CurrentCommands.Register` (with a `CalendarStore(folder, TimeZoneInfo.Local)` and a `CalendarFeed` over a `SocketsHttpHandler` with `MaxAutomaticRedirections = 5`) + `EncryptionCommands.Register`, all stores sharing one `Encryption(folder)`; passes the registry, a `TodoSidebar`,
    the `Encryption` and `AnsiConsole.Console` to `App`, returns `App.Run()`); the loop, layout and dispatch live in `App.cs`, commands in `CommandRegistry.cs`,
    input in `LineEditor.cs`, the command popup's rows and `PopupArea` in `CommandPopup.cs`,
    `/new`, `/notes`, `/open`, `/export`, `/import` in `NoteCommands.cs`, `/encrypt`, `/decrypt` in
    `EncryptionCommands.cs`, the masked prompt in `PassphrasePrompt.cs`, `/todo` and `/done` in `TodoCommands.cs`, the sidebar body
    in `TodoSidebar.cs`, `/tags`, `/links` in `TagCommands.cs`, `/current` in `CurrentCommands.cs`, and
    `/calendar-link` in `CalendarLinkCommands.cs`. Code that needs Spectre stays here; data and storage go to Core.
  - `src/Context.Desktop/`: the desktop app (`Microsoft.NET.Sdk.Razor`, `WinExe`, references `Context.Core`
    only, never Spectre or the console app; components and classes in `namespace Context`). `Program.cs` only wires
    things up (stores built like the console's, one `Encryption(folder)`, Photino window `Context` 1100x750, dev tools
    only in Debug). `DesktopState` builds a `DesktopSnapshot` (`Locked`, `Failed`, `Today`, `CurrentView? Current`,
    `Todos`, `Notes`, unreadable flags, `Folder`) off the UI thread with `Refresh()` (one at a time; requests during a run
    coalesce into one more run) and raises `Changed`; `Tick()` refreshes when the day changed or 5 min passed.
    `DataWatcher` (one `FileSystemWatcher` with subfolders, `IsRelevant` filter, 300 ms debounce, 30 s tick timer;
    never creates the folder) calls them. Components (`App`, `Main`, `CurrentPanel`, `TodoPanel`, `NotesPanel`,
    `LockedPanel`) inherit `StateComponent`, which injects the state and `DesktopActions` (internal types can't be
    public parameters). `DesktopActions` (with `ActionResult`: `ActionKind` `Done`/`Refused`/`Failed`, `ActionReason`,
    raw text) is the only write path: it never throws, runs one action at a time (`Busy`), refuses every action while
    `encryption.json` exists (`Locked`) without touching a file, refreshes the state after a change and has the test
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
    the move; CRLF and BOM kept). `TagPanel` lists `DesktopState.NotesWithTag`. `CommandPalette` (Ctrl/Cmd+K) ranks
    `/current`, `/open`, `/save`, `/tags`, `/todo` with `CommandMatcher` (shared with `CommandRegistry.Match`).
    `TodoPanel` has an entry row (`DesktopActions.AddTodo`). The page editor is `wwwroot/editor/editor.js`
    (`globalThis.ContextEditor`), built from `editor/src/` (`schema.js`, `markdown.js` with markdown-it `html: false`,
    `view.js`, `index.js`); `isLossless` false shows the grey `Saving will reformat parts of this note.`
    `wwwroot/index.html` (CSP meta tag, only `_framework/blazor.webview.js` and `editor/editor.js`), `wwwroot/app.css`
    and `wwwroot/editor/editor.css`.
  - **Desktop rules:** the Core writing calls (`Create`, `Save`, `SaveIfUnchanged`, `Add`, `Complete`, `Set`, `Clear`, `Delete`,
    `ClearCache`, `Migrate`, `DecryptAll`, `Vault.Create`) appear only in `DesktopActions.cs` (`DotNetObjectReference.Create(` once in `EditorInterop.cs` is the one exemption; the reads still write
    only through `TagIndex.Sync` and `CalendarFeed.Fetch`, as in the console). `DesktopActions` is named only by
    `.razor` files, `Program.cs` and `StateComponent`, never by `DesktopState`, `DataWatcher` or `DesktopSnapshot`, so
    nothing writes on render, refresh, the watcher or the tick. In `.razor` files every `@code` method that calls
    `Actions.` is bound by an event attribute in the same file (`@on<event>=` on an element or an `On<Event>=`
    callback parameter on a component), is no lifecycle method (`OnInitialized*`, `OnParametersSet*`,
    `OnAfterRender*`, `SetParametersAsync`, `BuildRenderTree`) and is called by no other method; no `.cs` file calls
    `Actions.`. Destructive actions take a `Confirmation`, and `new Confirmation(` appears only in
    `ConfirmDialog.razor`. With `encryption.json` it shows only `LockedPanel` and reads nothing.
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
    `ViewParityTests.cs` (golden ANSI output of `/current`, `/notes`, `/tags`, `/links`, `/calendar-link`; change
    its expected strings only when a spec changes that output), `CoreDependencyTests.cs` (architecture test: fails on any
    Spectre assembly, package, project reference or source mention in `Context.Core`), plus shared helpers such as `AnsiWrites.cs` (splits ANSI output on cursor moves),
    `SampleData` in `EncryptionTests.cs` (one file of every kind with plaintext markers), `Session` in
    `EncryptionCommandsTests.cs` (everything wired like `Program.cs`) and `FakeHttpHandler.cs` (canned HTTP responses). Note, todo, tag and calendar tests use their
    own temp folder (deleted in `Dispose`), never the real app data folder.
  - `tests/Context.Desktop.Tests/`: xUnit + bunit, references `Context.Desktop` and `Context.Core`; one test class per
    component or class, plus `EditorBundleTests` (Jint on the committed `editor.js` against `RoundTrip/lossless` and
    `RoundTrip/lossy` fixtures, compared with the C# parsers; `.gitattributes` keeps their bytes), `EditorJs` (bunit
    JSInterop setups for the page editor), `EditingFlowTests` and `TodoEntryTests`, `DesktopDependencyTests` (no Spectre, no Jint
    outside tests, exact npm versions), `DesktopSourceTests` (raw markup, risky elements,
    CSP, write sites, event-handler-only actions, button types, checkbox-only inputs, JS interop), `DesktopFixture` (temp folder, injected
    day/clocks, `FakeHttpHandler` linked from `Context.Tests`, a `BunitContext` serving the state and the actions),
    `FileSnapshot` (bytes and last-write times of a folder, to prove nothing changed) and `RepoFiles`.
- **Style:**
  - Write all output through an injected `IAnsiConsole`, never `Console.*` or the static
    `AnsiConsole.*` methods (except in `Program.cs`). Tests use `TestConsole` and assert on `.Output`.
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
