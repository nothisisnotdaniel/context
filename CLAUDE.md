# Context — conventions

Every factory agent reads this file before working on Context. Keep it short and accurate.

- **What it is:** a personal app for keeping information in one place, with two front ends: a desktop app
  (`Context.Desktop`) and a scriptable command-line tool (`context`). The user stores
  notes as Markdown files in a `Context` folder inside their app data folder
  (`Environment.SpecialFolder.ApplicationData`). It shows the day's appointments from `.ics`
  files in `<root>/calendar/`; later it will pull in information from email and other sources.
- **Tags:** the user places tags in the Markdown files, and the tags link related files together.
  The links between files are stored in a database that lives in the same `Context` app data
  folder. The Markdown files stay the source of the content.
  - `TagParser.Parse` finds `#tag` (at line start or after whitespace, a letter first, then letters,
    digits, `-`, `_`; at most 50 chars; lower-cased, distinct) and `#[tag with spaces]` (`#[` at line start or
    after whitespace, up to the first `]` on the line, not followed by `(`; trimmed, whitespace runs become one
    space, then the same rules plus spaces) outside fenced code blocks and inline backtick spans (a run of backticks up to the next run of the same length on
    the line; a run with no match is plain text). Code spans come from `CodeSpans.For(line)` (in `TagParser.cs`; `null`
    for a line with no backtick, else every run's match found in one pass, so a line full of stray backticks stays
    linear), shared by the tag, link and mention parsers. `TagParser.Collapse` is the one whitespace-collapsing loop
    (`CollapseWhitespace`, `NormalizeBracket` with its early exit, and `PersonParser.Normalize` use it). `#work` and
    `#[Work]` are one tag. `TagParser.Format` shows a tag as `#tag`, or `#[tag]` when it has a space; all tag
    output uses it. `TagIndex.NormalizeTag` (for a typed tag, as in `context tags <tag>`) accepts `work`, `#work`, `[a b]`, `#[a b]`. `TagIndex` keeps the index in SQLite at `<root>/context.db` (`Microsoft.Data.Sqlite`,
    `Pooling=False`, parameterized SQL only): `notes(name, modified)` (last-write UTC ticks, NULL for a
    note that couldn't be read so it is retried), `note_tags(note, tag)`, `note_links(note, target, heading)` (job 205: `heading` is the text after the first `#` or NULL; primary key stays `(note, target)`; a database without the column gets it through `ALTER TABLE` on every open, before any query; job 295: also `whole_key` and `part_key`, the upper-invariant of the target and of `NoteLinkParser.Split(target).Note`, NULL when empty, each with an index (`note_links_whole_key`, `note_links_part_key`), added the same way), `note_people(note, person, spelling)`, `note_decisions(note, line, text)` (job 180, primary key `(note, line)`; one row per `DecisionParser` line, written and pruned with the note's other rows), `note_todos(note, line, due, text, raw)` (job 201, primary key `(note, line)`; one row per open `NoteTodoParser` line, `due` `yyyy-MM-dd` or NULL, written and pruned like `note_decisions`), `note_topics(note, line, person, text, raw)` (job 253, primary key `(note, line)`; one row per `TopicParser` line, written and pruned like `note_decisions`; read by `Topics()`, plain notes only), `note_reviews(note, line, due, raw)` (job 256, primary key `(note)`; the first `ReviewDateParser` line of a plain note, `due` `yyyy-MM-dd`, written and pruned like `note_decisions`; read by `Reviews()`) and `note_attributes(note, key, value_key, value)` (job 157, primary key `(note, key, value_key)`, index on `(key, value_key)`) and `note_text` (job 170: FTS5 `fts5(note UNINDEXED, body, tokenize='trigram')` with FTS5 `secure-delete` on, set once in `Sync` after checking `note_text_config`; one row per plain note with its whole text, none for a note that can't be read or is over `NoteStore.MaxNoteBytes` (job 275: such a note is treated as unreadable, so it has no tag, link, people, attribute, decision, todo, topic, review or text rows and a NULL stored time); sensitive notes are never listed, so never indexed; every connection sets `PRAGMA secure_delete = ON`, so a deleted note's text doesn't linger in free pages of `context.db`; the rollback journal `context.db-journal` briefly holds the old pages and is unlinked, not zeroed, after each commit, which is outside this guarantee), `note_text_rows(note, row)` (job 295: note name -> its `note_text` rowid; re-indexing, deleting or pruning a note deletes its `note_text` row by that rowid, never `WHERE note`, and only while that row still names the note; the orphan prune goes through it; every non-upgrade `Sync` first drops each `note_text` row that is not its note's mapped row and each mapping whose row is gone or names another note, as an older version writing the same `context.db` leaves them, and clears those notes' stored time so they are re-parsed in that sync) and `note_skipped(note)` (job 295: one row per note over the cap, gone once the note is back under it, unreadable for another reason or gone). The cap counts the bytes actually read (`BoundedFile.Read` stops in its read loop): a note of exactly `MaxNoteBytes` bytes, a BOM or invalid UTF-8 bytes included, is indexed (invalid bytes decode as U+FFFD), one byte more is not. `Sync(NoteStore)` updates it
    in one transaction and only re-parses files whose time changed. `PRAGMA user_version` holds the parser
    version (`TagIndex.ParserVersion`, now 14: `note_text_rows`, `note_skipped` and the `note_links` key columns; 13 was `#<n>` work item references in `note_items`; 12 was `review:yyyy-MM-dd` lines in `note_reviews`; 11 was `next: @[Name] topic` lines in `note_topics`, no longer note todos; 10 was the heading of a `[[Note#Heading]]` link in `note_links.heading`; 9 was open `- [ ]` lines in `note_todos`; 8 was `Decision:` lines in `note_decisions`; 7 was note text in `note_text`; 6 was a link target that runs into a code span is no link); when it is lower, `Sync` clears every stored time and empties `note_text` and `note_text_rows` in the same
    transaction (so all notes are re-parsed and their text written once) and then sets it. Bump it whenever parsing changes. Queries: `Tags()`, `Reviews()`, `NotesWithTag`,
    `NoteTags()`, `FindNote`, `Links` (job 295: reads `note_links` only through `TagIndex.LinksSql`, `WHERE note = $note OR whole_key IN ($k, $kmd) OR part_key IN ($k, $kmd)` with `$k` the upper-invariant name and `$kmd` = `$k` + `.MD`, so no `SCAN note_links`; then `NameLookup.ResolveLink` as before), `Skipped()` (job 295: the `note_skipped` names, ignoring case, then ordinal), `People()`, `NotesMentioning`, `NotesWithAttribute(key, value)` (by `AttributeParser.ValueKey`), `NotesWithoutAttribute(key)`, `AttributeValues(key)` (job 291: the `(Note, Value)` rows of `note_attributes` for the key, value as stored, by note (ordinal), then `rowid`), `Search(text, select)` (job 295: calls `select(note, body)` per row of `note_text MATCH $q` and keeps the non-null results, so no body is held after its row; `$q` the text as one FTS5 phrase: `"` + text with `"` doubled + `"`, always a parameter, so FTS syntax is literal), `Decisions(tag?)` (`(Note, Line, Text, Modified)` rows of `note_decisions` joined to `notes` with a stored time, `Modified` UTC; a tag keeps the notes with that `note_tags` row), `NoteTodos()` (`NoteTodo` rows of `note_todos` joined to `notes` with a stored time). A database that can't be opened or isn't valid throws `TagIndexException` (an
    `IOException`) and is never deleted or overwritten. The file database waits up to `TagIndex.BusyTimeout` (5 s; an
    internal constructor argument shortens it for tests) for another process's lock, and `Sync` starts with
    `BEGIN IMMEDIATE`, so the desktop app and a command-line run can share it. `TagsView.Build`, `LinksView.Build`
    and `CurrentView.Build` call `Sync` first and report that error as a view kind or flag (`IndexUnreadable`); the
    command line (`tags`, `current`) then writes `Can't read context.db.` to stderr and exits 1, the desktop shows it.
  - **Note links:** `NoteLinkParser.Parse` finds `[[Note name]]` anywhere in a line (`[[`, text with no `[` up to the
    first `]` on the line, then `]]`; trimmed; empty or over 403 chars (`MaxTargetLength` = `NoteStore.MaxNameLength + 3`) is no link; distinct ignoring
    case, first spelling kept), skipping fences and code spans through `TagParser.LinesOutsideFences` and
    `CodeSpans` (shared, not copied). A backtick run that opens a matched code span between `[[` and that first `]`
    means no link, and the scan goes on at that span (job 163: ``[[a `b]] c` `` is no link; ``[[a`b]]`` links to ``a`b``). It scans
    each line once (cached next `]`, `[` and code-span start). `[[...]]` is never
    a tag and `#[...]` never a link; `|alias` is part of the target. `NoteLinkParser.FindSpans(markdown)` (job 206) is the
    same scan (`ParseLine`, `TagParser.IndexedLinesOutsideFences`) with every link, repeats included, as
    `NoteLinkSpan(Line, Offset, Target)` (`Offset` is the trimmed target's index in the text); its distinct targets are
    exactly `Parse`'s. **Link rewriting on rename** (job 206, `NoteLinkRewriter.cs`, pure strings):
    `Rewrite(text, NameLookup before, oldName, newName)` replaces only the note-part characters of each link whose text
    before the first `|` resolves through `before.ResolveLink` to `oldName` or `<oldName>/...` with the new full name
    (a written trailing `.md`, `#heading`, `|alias`, spaces, CRLF and BOM stay; code, `[[#Intro]]`, other and missing
    notes stay); the same instance when nothing changes or the names differ only in case. `TryRewrite(..., after, ...)`
    also checks the round trip (every link parses through `FindSpans` in the same place and each rewritten one resolves
    through the post-rename names to the expected name and heading; a new title with `#`, `|`, `[`, `]` or a backtick
    fails when a link changes). **Heading links** (job 205): `NoteLinkParser.Split(target)`
    gives `NoteLinkTarget(Note, Heading?)`, split at the first `#`, both trimmed, `Heading` null when nothing follows the `#`
    (`[[#Intro]]` has an empty note part: a heading of the note it is in). Targets are stored as written and
    resolved only at query time by `NameLookup.ResolveLink` (the whole target through `NameLookup.Resolve` first, with no heading, so
    `[[C# notes]]` finds a note named `C# notes`; then the note part with its heading; `Resolve` is like `context note show`: as written, then without one `.md`; exact, then
    ignoring case), never turned into a path, so a link to a missing note starts working once the note exists. `Links`
    merges tag links and note links into one `NoteLink` per other note (`Direction` `None`/`To`/`From`/`Both`,
    `Missing` = a `To` target that names no note, one row per note part (`Gamma#A` and `gamma` are one), shown as the
    first stored spelling of that part (rows read `ORDER BY note, target`); `[[#Intro]]` gives no row); never the note itself. The desktop shows
    the direction as `links to`, `linked from`, `both ways` or `links to (missing)` (nothing for a link by shared
    tags only) in `LinksPanel`; the command line has no links command (removed in job 173).
  - **People:** (job 156) `PersonParser.Parse` finds `@[Name]` (`@[` at line start or after whitespace, up to the first
    `]` on the line, not followed by `(`; trimmed, whitespace runs become one space; must pass `NoteStore.ValidateTitle`,
    so `/ \ : ..` and the like are no mention; distinct ignoring case, first spelling kept), skipping fences and code
    spans through the shared `TagParser` helpers. `@[...]`, `#[...]` and `[[...]]` never match each other's parser.
    `note_people` keys a mention by `PersonParser.Key` (lower case) and keeps the spelling. A person's page is the note
    `people/<Name>` (`People.PersonNote`, `People.PersonOf`: a direct subpage of the top-level `people`, ignoring case);
    its own mentions never count. `People()` gives each person once with the number of mentioning notes, shown by the
    page's title, else the ordinal-first spelling. `IcsParser` reads each `ATTENDEE`'s `CN` (normalized like a mention)
    into `Appointment.Attendees`; the address is never read out. `PersonView.Build` lists the mentioning notes and the
    appointments (calendar files and the cached Exchange copy, no download) whose title or location mentions the
    person or whose attendee matches, latest first. **Todos** (job 159): `PersonView.Build(..., todos)` (optional
    `TodoStore?`) adds `OpenTodos` (`List()` order) and `DoneTodos` (`ListDone()` order) whose text mentions the person
    (`PersonParser.Parse` + `aliases.PersonKey` at query time, as for appointments; nothing stored in `context.db`, no
    `ParserVersion` change), `TodosUnreadable` for a bad `todos.json` and `DoneDate(todo)` (the day in `Zone`); todo
    mentions count for `Kind`. `PersonParser.Segments(line)` splits one-line text into `PersonSegment(Text, Name)`
    (plain: `Name` null; every mention occurrence, same rules as `Parse`; joined texts give the input). The desktop
    `TodoPanel` draws each mention as `<button class="mention">` (raw text, full text in `title`) inside the todo's
    `<label>`, click stopped there; `OnPerson` (normalized name) goes to `Main.OpenMention`. `PersonPanel` adds
    `Open todos` (due, text) and `Done todos` (done date, text), each only when non-empty, no checkboxes; the red
    `Can't read the todos.` line for a bad `todos.json`; empty line `No notes, appointments or todos mention <name>.`
    The editor shows a mention as a `mention` chip; following it calls
    `DesktopActions.OpenPerson`, which creates `people` (`# people\n`) and the page (`CreateChild`) only on that click,
    and refuses when the top-level `people` is sensitive. The new page gets the `person` type's template
    (`PageTypes.Template`: a blank line and one `<attr>::` line per attribute) through `CreateChild(parent, title, body)`.
    **Aliases** (job 158, `PersonAliases.cs`): `PersonAliases.Parse(attributes, title)` splits every `aliases::` value
    at `,`, normalizes each part with `PersonParser.Normalize` (empty/invalid skipped), distinct by `Key` (first
    spelling kept), the page's own title dropped. Only direct person pages (`People.PersonOf`, any `type::`) supply
    them; other notes, subpages, `types/` pages and sealed files don't. The one rule, `Resolve(name)`: a key equal to a
    person page's title (ignoring case) is that person, else the alias of exactly one person page is that person
    (`Person(title)`), else `Ambiguous(titles)` (sorted ignoring case, then ordinal) or `None`; an unresolved key is
    its own person entry. `PersonKey(name)` is the key mentions group by; `Clashes(title)` gives the page's aliases
    that are shared (`Alias <a> is also used by <titles>, so @[<a>] links to no one.`) or another person's title
    (`Alias <a> is another person's name, so @[<a>] links to them.`); `AmbiguousText` is
    `@[<name>] is an alias of more than one person: <titles>.` (all raw; front ends clean). `TagIndex.Aliases()` builds
    the rule at query time from the person pages in `notes` and their `note_attributes` rows (`key = 'aliases'`);
    `People()` (each note counted once per resolved person), `NotesMentioning` and `PersonName` resolve both the typed
    name and every stored mention through it, so an alias edit counts on the next `Sync` with no rescan and no
    `ParserVersion` change. `PersonAliases.Load(store)` reads the person pages directly (never throws), used by
    `OpenPerson` and by `PersonView` when the index can't be read. `PersonView` resolves the typed name and
    appointment title/location mentions and attendee names the same way, and has `AliasClashes` (when the name is a
    person page) and `AmbiguousWith`. `OpenPerson` opens the resolved person's page and refuses an
    ambiguous name (`ActionReason.Ambiguous`, the hint as text, nothing written).
  - **Work items** (job 280, `WorkItemParser.cs`, `WorkItemView.cs`, `GitCommitLog.cs`, `DesktopRequest.cs`):
    `WorkItemParser.Parse` finds `#<n>` (Times' work item numbers) at line start or after whitespace: 1 to 9 ASCII
    digits, the first 1-9, not followed by a letter, digit, `-`, `_` or `#`; outside fences and code spans (shared
    helpers); distinct, first-seen order; never a tag. `TryParseNumber` reads a typed number (optional `#`).
    `note_items(note, item)` holds them per plain note (written and pruned like `note_decisions`);
    `TagIndex.NotesWithItem(n)` leaves out the item's own page. A work item's page is `items/<n>` (`WorkItems.PageNote`,
    `ItemOf`: a direct subpage of the top-level `items`, ignoring case); `WorkItems.EnsurePage` creates `items`
    (`# items\n`) and the page (`# <n>\n`) when missing, refusing a sensitive top-level `items` with nothing written.
    `WorkItemView.Build(index, store, todos, calendar, feed, timesFolder, repositories, ICommitLog, n)` never throws:
    `Notes`, `OpenTodos`/`DoneTodos` (`todos.json` texts), `People` (resolved through `Aliases()`, count desc, then
    name), `Meetings` (files plus cached feed, no download, latest first), `Hours` (Times day files, `WorkItem == n`
    groups per `ISOWeek` `yyyy-Www`, newest first, `Total`, `SkippedFiles`; the Times folder is never written),
    `Commits` (repo folder name, hash, date, subject; newest first) and the flags `IndexUnreadable`,
    `TodosUnreadable`, `TimesKind` (`NotSetUp`/`Unlistable`/`Read`), `CommitsKind` (`NotSetUp`/`GitMissing`/`Read`),
    `FailedRepos`. `GitCommitLog` runs `git --no-pager -c core.fsmonitor=false -C <folder> log --all --no-color -F
    --grep=#<n> -n 500 --format=...` through `ArgumentList` (no shell), `GIT_TERMINAL_PROMPT=0`, a 10 s timeout (killed)
    and a 1 MB output cap, re-checks each message with the parser and keeps at most 100 per repo; tests use
    `FakeCommitLog` (and one real temp repo in `GitCommitLogTests`). The repository folders are
    `"repositories":["<absolute folder>"]` in `settings.json` (`SettingsStore.ReadRepositories`/`SaveRepositories`,
    `Repositories.CheckAdd`: trimmed, absolute, at most 400 chars, existing, distinct ignoring case, at most 20; other
    properties kept, a broken file never overwritten). Desktop: `ItemPanel` sits above `LinksPanel` on `items/<n>` with
    `Notes` (`RequestOpen`), `Open todos`, `Done todos`, `People` (`OpenMention`), `Meetings`, `Hours` (week rows and
    `Total`) and `Commits` (date, repo, 7-char hash, subject), each only with rows; grey `Times isn't set up, so no
    hours.`, `Set repository folders in Settings to see commits.`, `No notes, todos or meetings mention #<n>.`; red
    `Can't read the mentions.`, `Can't read the todos.`, `Skipped <k> unreadable Times day file(s).`, `Can't run git, so
    no commits.`, `Can't read commits in <repo>.`, plus `Can't read Times' folder.` (`Unlistable`) and `Can't read settings.json.`; the `PersonPanel` read-only, `indexGate`, reload and stale-result
    rules (`DesktopState.Item(n)`). `DesktopActions.OpenItem(n)` and `AddRepository`/`RemoveRepository` (the
    Settings *Repository folders* section: text field + Add, Remove per row) are the writers. **Open from the command
    line:** `context item <n>` prints `Item #<n>`, `Notes`, `Todos`, `People`, `Meetings`, `Hours`, `Commits` (every
    heading always; not set up `- Times isn't set up.` / `- No repository folders set.`; read errors on stderr, exit
    1, the rest printed; a bad or second number is a usage error before the folder is read); `--json` writes
    `{item,notes,todos,people,meetings,hours,commits}` (see `CliJsonTests`). `--open` (not with `--json`) ensures the
    page (`Created items/<n>.`), writes `<root>/.desktop/open-request` (`item <n>\n`, `PrivateFile` temp + move,
    `0600`, folder `0700`; never a path) and, when `<root>/.desktop/running.lock` can be opened (no desktop holds it),
    calls `IDesktopLauncher` (`ProcessDesktopLauncher`: macOS `open -b com.nothisisnotdaniel.context`, Windows the
    installed `%LOCALAPPDATA%\ContextDesktop\current\Context.Desktop.exe`, else unsupported; failure `Can't start the
    desktop app.`, exit 1, the request stays), then `Opened #<n>.`. The desktop's `Program.cs` holds the lock open
    (`PrivateFile.OpenLock`) while it runs, and `OpenRequestWatcher` (one non-recursive watcher on `.desktop`, also at
    start) takes each request through `DesktopRequest.TryTake` (at most 64 bytes through `BoundedFile`, deleted, only
    exactly `item <n>` raises `ItemRequested`), then `DesktopActions.OpenRequestedItem` offers the page
    (`DesktopState.OfferItemPage`/`TakeItemPage`) and `Main` opens it through `RequestOpen` (unsaved text saved first,
    else the discard dialog). **Second copy** (job 315): `DesktopStartup.Begin(root, crashLog, isWindows)` (called by
    `Program.cs` right after the crash log is built, before anything else) takes the lock first and returns
    `DesktopStart(Lock, Hint, HandOver)`: taken, it calls `CrashLog.Begin()`; held by another holder (`IOException`), it
    never touches the session (no hint, no `unclean-exit` log), and on Windows writes the show request
    (`DesktopRequest.WriteShow`: `show\n`, same temp + move) and hands over, so `Main` returns at once (no window, no
    `End()`); elsewhere the copy runs as before; can't be made (folder or access), no lock and `Begin()` as before;
    never throws. The lock is held until `Main` exits. `DesktopRequest.IsShow` accepts exactly `show` (optional final
    `\n`/`\r\n`), `TryTake(root, out item, out show)` tells it apart, and the watcher raises `ShowRequested`, which
    `Program.cs` turns into restore-and-raise of the main window on its thread (`SetMinimized(false)`, `SetTopMost`
    on and off). Tests wait on the watcher's `Processed` count with a bounded `SpinWait`.
  - **Attributes and page types:** (job 157) `AttributeParser.Parse` finds `key:: value` lines outside fences (key at
    column 0: a letter, then letters, digits, `-`, `_`, at most 50, lower-cased invariant, directly followed by `::`;
    value trimmed, whitespace runs one space, 1 to 400 chars, so `role::` is no attribute); a key may repeat, pairs
    are distinct by key and `ValueKey` (exactly one `[[Target]]` gives the trimmed target; lower-cased), first
    spelling kept. `NormalizeKey` checks a typed key. `PageTypes.Load(store)` gives the built-ins `organization` (no
    attributes) and `person` (`aliases`, `affiliation`, `role`), each replaced by a direct subpage of the top-level `types`
    (ignoring case) of that title; its `attribute::` lines (valid keys, distinct) are the attributes. A sensitive
    `types` reads no type page; an unreadable one is skipped; never throws. `TypesOf` reads `type::` values, a person
    page without one is `person`. `FindView.Build(index, store, key, value)` (`Found`, `NoMatches`, `InvalidQuery`
    without reading, `IndexUnreadable`) syncs and lists the matching notes sorted ignoring case then ordinal;
    `type person` also lists untyped person pages. An attribute value `[[X]]` is a note link through the link parser.
    The command line has `context find <attribute> <value...>`; the desktop `/find` and the type pages.
  - **Full-text search** (job 170, `SearchView.cs`): `SearchView.Build(index, store, text)` trims the text; under
    `MinQueryLength` (3) is `TooShort`, over `MaxQueryLength` (200) `TooLong`, both without opening the index; else
    `Sync`, `TagIndex.Search` (the trigram match is only a pre-filter), and per row the first line (split at `\n`, a
    trailing `\r` dropped) containing the query by `OrdinalIgnoreCase` as `SearchHit(Note, LineNumber, Line)` (1-based; job 285 adds `MatchStart`, the match's index in `Line`, `Types`,
    `PageTypes.TypesOf` of the body, and `Modified`, the `store.List()` last-write time converted to UTC, null when not
    listed); a
    row with no such line is dropped (so no match across lines). Hits sorted by note ignoring case, then ordinal;
    `TagIndexException` gives `IndexUnreadable`; kinds `Found`, `NoMatches`. The command line has `context search
    <text...>`, the desktop `/search` (`SearchPanel`). Out of scope: sensitive notes, todos, calendar, ranking,
    highlighting, fuzzy/stemmed search, diacritic folding.
    **Tag filters** (job 207, `TagFilter.cs`): `TagFilter.IsTagFilter(text)` is true when the trimmed text starts with
    `#` or with `not` + whitespace + `#` (ignoring case); `TagFilter.TryParse` reads `term (and term)*`, a term an
    optional `not` and one tag written as in notes (`#tag` or `#[tag with spaces]`, letter first, so `#1234` is not a
    tag; normalized by `TagIndex.NormalizeTag`), `and`/`not` ignoring case, at least one term without `not`; `or`,
    parentheses, bare words and invalid tags fail. `TagFilterView.Build(index, store, text)`: over 200 chars (trimmed)
    `TooLong`, then `Invalid`, both without opening the index; else `Sync` and `NoteTags()` matched in memory (every
    included tag, no excluded one; no user text reaches SQL), notes sorted ignoring case then ordinal; kinds `Found`,
    `NoMatches`, `IndexUnreadable` (a `TagIndexException`). Sensitive notes are never indexed, so never listed. Desktop
    only (`SearchPanel`, saved searches).
  - **Decision log** (job 180, `DecisionParser.cs`, `DecisionsView.cs`): `DecisionParser.Parse(markdown)` finds lines
    outside fences (`TagParser.NumberedLinesOutsideFences`, 1-based numbers counting every line) that are optional
    spaces or tabs, an optional `-`, `*` or `+` and one space, then `Decision:` ignoring case; the rest is trimmed and
    collapsed (`TagParser.Collapse`), cut to `MaxTextLength` (1000) without splitting a surrogate pair; an empty rest,
    `Decisions:`, text before it or `1. ` is no decision (`ParsedDecision(Line, Text)`). `DecisionsView.TryNormalizeTag`
    (blank: no filter; else `NormalizeTag`, valid only when `NormalizeBracket` keeps it). `DecisionsView.Build(index,
    store, tag, zone)`: an invalid tag is `InvalidTag` without opening the index; else `Sync`, `TagIndex.Decisions`,
    each as `Decision(Note, Line, Date, Text, People)`: `Date` is the title's (last segment's) leading `yyyy-MM-dd`
    (the whole title or followed by whitespace, a real date), else the last write time as a day in `zone`; `People`
    the line's `PersonParser.Parse` mentions shown as `TagIndex.Aliases()` resolves them (else as written), distinct
    by `PersonParser.Key`, in line order. Sorted by date descending, then note ignoring case, then ordinal, then line;
    kinds `Found`, `NoDecisions`, `InvalidTag`, `IndexUnreadable` (`TagIndexException`). Sensitive notes never appear.
  - **Similar notes** (job 268, `SimilarNotes.cs`): `SimilarNotes.TitleKey(name)` is the last segment's
    letter-or-digit runs, lower-cased invariant, joined by one space. `TitleMatch(a, b)`: no letter in either key is
    `None`; equal keys `Same`; keys equal once digits are removed (and different) `None`; a key under 5 characters
    `None`; else `Similar` when the banded `Distance` is at most 1 (shorter key under 10) or 2. `Shingles(text)` are the
    distinct runs of 3 tokens (`Tokens`: letter-or-digit runs, lower-cased); `ContentScore` is the Jaccard index, null
    under `MinShingles` (10) or below `ContentThreshold` (0.5, inclusive). `Pairs` buckets titles by key length and
    finds content candidates through an inverted shingle index, skipping a shingle shared by more than
    `MaxShingleNotes` (50) notes. `TagIndex.NoteTexts()` (no parameters) reads every `note_text` row, so only plain,
    readable notes up to `MaxEditBytes` take part. `SimilarNotesView.Build(index, store)` (`Found`, `NoSimilar`,
    `IndexUnreadable`): `Sync`, one `NoteTexts()`, only `store.List()` names; each pair once with `First` before `Second`
    ignoring case, then ordinal; pairs with both reasons first, then score descending, then names. Desktop only.
  - **Tag board** (job 242, `BoardView.cs`): boards are set by hand in `settings.json` as
    `"boards":[{"tag":"launch","columns":["todo","doing","done"]}]` (`SettingsStore.ReadBoards`, `BoardSetting`; at most
    `MaxBoards` 50 boards and `MaxColumns` 12 columns; a column is ASCII letters, digits, `-` and `_`, a letter first, at
    most 40 characters, lower-cased, distinct; anything else throws `SettingsStoreException`; every write keeps
    `boards`). A card's column is its first `#status/<column>` token (`#status/` ASCII-case-insensitive, at line start
    or after whitespace, outside fences and code spans, the longest run of column characters, one of the board's
    columns; else plain text); `TagParser` is unchanged, so `#status/doing` is also the tag `status/doing`.
    `BoardView.Build(store, index, board)`: `Sync`, a note card per note with the tag (its token outside open todo
    lines) and a todo card per open note todo whose own text has the tag (its own token); sensitive and unreadable
    notes never appear; a `TagIndexException` is `Unreadable`. Columns in board order, led by No status (`Name` null)
    only when it has cards; cards by note ignoring case, then ordinal, the note card first, then by line.
    `BoardView.Move(store, card, column, history)` rewrites only the token's column (none: a final `#status/<col>`
    line, or ` #status/<col>` at the todo line's end) through `ReadForEdit`/`SaveIfUnchanged` (CRLF and BOM kept,
    a version kept): `Done`, `Unchanged` (own column, nothing written), `Changed` (the token's line is not as built,
    or the file changed), `Missing`, `Failed`.
- **Views:**
  - **Review view (job 169):** `ReviewView.Build(notes, todos, calendar, feed, today)` in Core (`ReviewView.cs`) holds
    `From` (today-6) and `To` (today), the calendar's `Zone`, `ChangedNotes` (`ChangedNote`: name and last-write time
    converted to the zone; no sensitive notes; days From..To in the zone; newest first, then name ignoring case),
    `Done` (job 293: the done log's entries dated From..To, in `DoneLogView` order; `todos.json` done todos are no
    longer shown), `DoneLogUnreadable` (`Done` empty, the rest still filled), `Overdue` (open, due < today, `List`
    order), `TodosUnreadable` (only `Overdue` empty, the rest still filled), seven `ReviewDay`s today+1..today+7
    (`CalendarStore.Day` with the feed's events; `TimeText` as `CurrentView.Time`), and `Folder`,
    `UnreadableCalendarFiles` (each once), `Status`, `CachedAtUtc`, `Reason` like the current view. The feed is fetched
    once per build; it never opens `context.db`. `context review` prints it; the desktop's `ReviewPanel` (`/review`)
    draws it.
  - **Done log (job 293):** `DoneLog.cs` in Core. Every completed todo is appended to the ordinary note `done.md`
    (`DoneLog.NoteName` `done`, found in any case) as `DoneLog.Line(day, text, due)`: `- yyyy-MM-dd <text>` (text
    through `Inbox.Clean`, tags kept) plus ` (due yyyy-MM-dd)` when the todo had a due date. `DoneLog.Append(store,
    day, text, due)` shares the inbox capture's loop (`Inbox.AppendLine`: a missing note is created as `# done\n` plus
    the line, a missing final newline added first, every other byte kept, CRLF and BOM included, `MaxRetries` retries
    on a change on disk; a sealed `done.ctxs` ends as `ChangedOnDisk`) and returns `DoneLogResult` (`Appended`,
    `ChangedOnDisk`, `TooLarge`, `Failed`); it never throws (an internal overload takes a `beforeSave` hook for tests).
    `DoneLog.Read`/`Parse` take the lines outside fences that are `- yyyy-MM-dd <text>` with a real date as
    `DoneEntry(Date, Text, Due?, Line)`; a trailing ` (due <real date>)` with text before it is the due date.
    `DoneLogView.Build(store)` groups them into `DoneWeek(Monday, Entries)` (Monday to Sunday), newest week first,
    inside a week by date then line descending; `Unreadable` (no weeks) when the note can't be read. The desktop's
    `CompleteTodoNow`/`CompleteNoteTodoNow` append after a Done with the local day (`localNow`); a failed append keeps
    `ActionKind.Done` with `ActionReason.DoneLogLeft`, and `TodoPanel`/`MeetingTodosPanel` show the red
    `DesktopActions.DoneLogLeftText` `Done, but it couldn't be added to the done log.` (`BeforeDoneLogSave` is the
    test hook). CLI `todo done` (stored and note todos) appends after its Done; a failed append writes
    `Couldn't add it to the done log.` to stderr, exit 0. A todo that isn't completed appends nothing. `DonePanel`
    (`section.page.done-panel`, `Page.Done`, palette `/donelog`, argument ignored) draws `DesktopState.DoneWeeks()`
    (off the UI thread, never throws: on an error an unreadable view): `Done log`, the icon-only close button, an
    `h3.done-week` `Week of yyyy-MM-dd` per week, `li.done-entry` with the date, the one-line text and `due
    yyyy-MM-dd` when set (`app.css` lays the rows out like the review's todo rows); grey `Nothing done yet.` without entries, red `Can't read the done log.` when unreadable;
    it reloads on `State.Changed` (also when `done.md` changes on disk) keeping the shown rows. `done.md` is an
    ordinary note: it is listed, searched and indexed like any other.
  - **Current view:** all relevant information for now: today's appointments, due todos and linked notes.
    `CurrentView.Build` in Core reads the data (including the untruncated appointments, the time text and the
    calendar's `Zone`, in which a front end shows the cache time); `context current` prints it as text; the desktop
    has no dashboard for it any more (job 216: `DesktopSnapshot.Current` feeds `/meeting <n>`'s numbering and the feed
    line in `DayAppointmentsPanel`). It holds the day (`Today`), the appointments, a `Due` list of every todo with
    `Due <= today` (date, text and `Overdue`; `DueTodo` has no number) and the linked notes. A todo links to notes
    through tags: `TagParser.Parse` on the text of each due todo (at run time, not stored), then
    `TagIndex.NotesWithTag` after `Sync`; each note once, sorted by name ignoring case, with its matching
    tags. Flags instead of exceptions: `TodosUnreadable` for a bad `todos.json` (the day and the appointments are
    still there) and `IndexUnreadable` for a bad `context.db` (the due todos are still there). With no appointment
    and nothing due the index is not synced. Order in both front ends: day, appointments, due todos, linked notes.
    **Times today** (job 277, Core `TimesNow.cs`): `TimesNow.Read(folder, today, now)` (never throws, never writes)
    gives `Kind` (`None`: null folder, no file, an absence day or no arrival; `Unreadable` (`TimesDay.Read`'s);
    `Booked`), `Arrival`, `Leaving` (only when `HasLeft`), `Left` (`TimesBalance.ReadTarget` minus
    `TimesDay.WorkTimeAt(now)`: `WorkTime` plus `now - last entry` while `IsWorking` and `now` is after it; can be
    negative) and `Checkpoint` (`TimesCheckpoint(WorkItem, Name)`: the last checkpoint with no later checkpoint, break
    start or leaving; else null). `FormatLeft` is Times' `H:mm`, truncated to whole minutes, `-` for negative, `0:00`
    for zero. It is not part of `CurrentView`: `context current` reads it for `today()` and `now()`, the desktop through
    `DesktopState.TimesNow()` (see Sidebar Times).
    **Meeting prep** (job 160, `MeetingPrep.cs`): `Related` (parallel to `Appointments`, same count and order) holds
    up to `MeetingPrep.MaxRelated` (3) `RelatedNote(Name, Tags, Words)` per appointment (the 3-argument `Related`; the
    overload with `int? max`, `null` uncapped, serves the desktop's appointment page, job 214). `MeetingPrep.Words(text)`:
    maximal `char.IsLetterOrDigit` runs, lower-cased invariant, without runs under 3 chars, digit-only runs and
    `StopWords` (English and German fillers such as `the`, `meeting`, `weekly`, `und`, `für`), each once. An
    appointment's words come from its `Title` and `Attendees` (never `Location`). A note matches through a tag when
    every `Words(tag)` (at least one) is an appointment word, and through a word of its `Title` (last segment). Ranked
    by `Tags.Count + Words.Count` descending, then name ignoring case, then ordinal. `Build` syncs once when there is
    an appointment or something due and reads the index through one `TagIndex.NoteTags()` (every `(note, tag)` row)
    plus `NoteStore.List()`, matching in memory (`TagIndex.Reads`, internal, counts `Run` calls for tests); only
    listed `.md` notes count, so sealed notes never appear. A bad `context.db` also empties every `Related` list.
    `context current` writes `related\t<name>\t<#tags then words>` after each appointment line and, with appointments,
    `Can't read context.db.` once on stderr (exit 1, after `Can't read todos.json.` when both). On the desktop the
    related notes show on the appointment page (job 214); the dashboard `CurrentPanel` that drew the rows, numbers
    and linked notes was removed in job 216.
  - **Calendar:** the user puts iCalendar files in `<root>/calendar/`. `CalendarStore(root, TimeZoneInfo)`
    reads only the top-level `*.ics` files (case-insensitive, no subfolders, never writes anything); a file over
    5 MB (`MaxFileBytes`) is not read. `Day(d)` returns a `CalendarDay`: the events whose local span overlaps
    `[d 00:00, d+1 00:00)` plus zero-length events starting on `d`, sorted all-day first, then start, then title
    (ordinal ignoring case), and the names of unreadable files (IO/access error, `FormatException`, too large).
    `IcsParser.Parse(text, zone)` scans linearly (no regexes): unfolds lines starting with space/tab, matches
    names ignoring case, unescapes `\n \N \, \; \\`, reads `SUMMARY`, `LOCATION`, `DTSTART`, `DTEND`, `STATUS`, `CLASS` of
    each `VEVENT` (nested components like `VALARM` ignored). Times: `…Z` (UTC), `;TZID=` (unknown id = local),
    floating (local), `;VALUE=DATE` (all-day, `DTEND` exclusive). No `DTEND`: ends at start (all-day: one day).
    Skips `STATUS:CANCELLED`, private events (job 227: `PRIVATE` or `CONFIDENTIAL`, trimmed, ignoring case, on any
    `CLASS` line; always on, no setting, so they never reach the sidebar, `/meeting`, `context current`, review or
    person pages; `calendar-link.ics` stays as received), missing/invalid `DTSTART`, end before start. No `BEGIN:VCALENDAR` throws
    `FormatException`. **Recurring events** (job 162): `IcsParser.Parse(text, zone, day)` (used by `Day` and for the
    feed in `CurrentView`) also reads `UID` (job 287: `Appointment.Uid` on every occurrence and override, and from
    `Parse(text, zone)` too; Outlook's is `outlook:` + `GlobalAppointmentID`; not part of `Equals`/`GetHashCode`), `RRULE`, `RDATE`, `EXDATE`, `RECURRENCE-ID` and returns every occurrence
    touching `day` (the `Overlaps` rule); `Parse(text, zone)` (`All`, `PersonView`, the feed check) still gives one
    entry per `VEVENT`, its `DTSTART`. `RecurrenceRule` (`Recurrence.cs`, day numbers only, so no `DateTime`
    overflow) supports `FREQ=DAILY|WEEKLY|MONTHLY|YEARLY`, `INTERVAL`, `COUNT` (DTSTART counts, excluded dates too),
    `UNTIL` (inclusive; date, floating or `…Z`), `BYDAY` (weekly list with `WKST`, monthly/yearly ordinals, daily
    filter), `BYMONTHDAY` (negative too), `BYMONTH`; DTSTART is always the first occurrence. Any other part
    (`BYSETPOS`, `BYWEEKNO`, `BYYEARDAY`, `BYHOUR`/`BYMINUTE`/`BYSECOND`, other `FREQ`), a bad value, `COUNT` with
    `UNTIL` or several `RRULE`s drop only the rule's occurrences. Occurrences keep DTSTART's wall clock in its zone
    and the event's duration, then are converted. `RDATE` adds (no duplicates; `PERIOD` ignored), `EXDATE` removes
    (a date value, or any value on an all-day event, removes that local day); an event with `RECURRENCE-ID` and the
    same `UID` replaces the occurrence starting then (cancelled or private: only removes); a cancelled or private series
    and its overrides show nothing. Bounds: without `COUNT` the rule skips ahead to the day; at most `RecurrenceRule.MaxPeriodsPerEvent`
    (50,000) periods per event and `RecurrenceBudget.StepsPerParse` (1,000,000) steps per `Parse`, after which events
    show only DTSTART and `RDATE`s. Not supported: `VTIMEZONE`, `EXRULE`, `RANGE=THISANDFUTURE`, occurrences on
    person pages. The current view shows each appointment with its time
    text (`CurrentView.TimeText`: `all day` or `HH:mm-HH:mm`, `…` for a side outside today, a midnight end shows
    as `00:00`), its one-line summary or `(no title)` and its one-line location; with none, `No appointments
    today. Put .ics files in <root>/calendar …`; then `Can't read calendar/<file>` per unreadable file
    (`calendar/` alone if the folder can't be listed; on the command line these lines go to stderr).
  - **Exchange link:** the desktop's settings page (see Settings) saves a published Outlook/Exchange ICS link,
    shows it masked with the last download time and removes it together with the cache; the command line has no
    command for it. `CalendarLinkView.Run(link, feed, argument)` in Core acts (a link saves it, a blank argument
    shows it, `off` removes the link and the cache) and returns a kind plus the masked link only.
    The link is a **bearer secret**: `CalendarLink(root)` keeps it in `<root>/calendar-link.txt`
    (one line, temp file + move, mode `0600` off Windows) and it is only ever shown as `CalendarLink.Mask`
    (`https://<host>/…`); never echo it, put it in another file or render an exception message that could hold
    it. `CalendarLink.Validate`: absolute `https` or `webcal` (stored as `https`), a host, no user-info (not even
    `@`), no whitespace. `CalendarFeed(root, link, HttpMessageHandler, utcNow, timeout?)` `Fetch()` never throws:
    `NotConfigured` (no request), the cache `<root>/calendar-link.ics` when younger than 5 min (no request), else
    one GET (10 s timeout, final URI must be `https`, 2xx, at most `CalendarStore.MaxFileBytes` by
    `Content-Length` and streamed bytes, must parse with `IcsParser`) that replaces the cache atomically and
    stamps its last-write time with `utcNow` (`Fresh`). Failures keep the cache untouched: `Stale` with it, or
    `Failed`; the reason is a fixed text (`timed out`, `network error`, `HTTP <code>`, `not HTTPS`, `over 5 MB`,
    `not a calendar`, `can't read calendar-link.txt`). The current view merges the feed's events through
    `CalendarStore.Day(d, extra)`; the front ends add `Exchange calendar offline, showing the copy
    from …` (Stale) or `Can't download the Exchange calendar (<reason>).` (Failed; stderr and exit code 1 on the
    command line). Recurring
    feed meetings show on every occurrence (see Recurring events). Tests use
    `FakeHttpHandler` and never touch the network; feed tests take their timeout from
    `CalendarFeedTests.TimeoutFor` (100 ms only with `FakeHttpHandler.Hang()`, 30 s for every handler that answers).
- **Data is plain, except sensitive notes:** every file in the data folder is stored as plain bytes, except the notes
  the desktop user marked sensitive (`<Title>.ctxs`, see Sensitive notes). Todos, the inbox, calendar files and
  `context.db` are never sealed.
- **Owner-only files** (job 274): off Windows every data file Context writes is `0600` and every data folder it creates
  (missing parents too) `0700`, both through `PrivateFile` (`Create`/`Write` with `CreateNew`, `CreateFolder`; `Tighten` sets
  an existing `context.db` with group or other bits to `0600` before SQLite opens it; `OpenLock(path)` (job 280) opens a lock file `OpenOrCreate` with `FileShare.None`, `0600` off Windows). `PrivateFileSourceTests` fails on
  any other `Directory.CreateDirectory(`, `File.WriteAll` or writing `new FileStream(` in `src/`, apart from its allow-list
  (`NoteStore.WritePlainCopy`: a path the user picks). Existing files Context doesn't rewrite keep their mode.
- **Sensitive notes:** (job 136, desktop only) a single note can be marked sensitive; it is then a sealed top-level
  `<Title>.ctxs` file instead of `<Title>.md`. The command line, `context.db` and search see only its
  name, marked `locked`.
  - **Top-level only** (job 152): a `.ctxs` below the top level is never listed. `MarkSensitive` is Refused/`Invalid`
    with no key call for a subpage (`Only top-level notes can be sensitive.`) and for a note with subpages (`A note
    with subpages can't be sensitive.`). A sensitive name blocks only top-level titles; a sensitive note's More actions
    menu has no `New subpage` and no `Move`.
  - **Core (managed, no crypto):** `NoteStore.SensitiveExtension` (`.ctxs`), `ListSensitive()` (top-level `*.ctxs` as
    `NoteInfo`), `TryFindSensitive(name, out NoteInfo?)` (matched against `ListSensitive()` only, exact then ignoring
    case, never turned into a path) and `DeleteSensitive(name)`. `List()`, `TryFind` and `Delete` stay `.md`-only;
    `Create`, `CreateWithText`, `Save` and `Rename` treat a sensitive name as taken (ignoring case). A `.ctxs` is never
    moved (its name is sealed in). `TagIndex` never reads one, so its tags and links are in no table and a `[[link]]`
    to it shows as missing.
  - **Command line:** `notes` prints `<name>\tlocked`; `note show` and `export` on one answer
    `<name> is sensitive. Open it in the desktop app.` (`Cli.SensitiveText`, exit 1); `import` and `capture` onto a
    sensitive name exit 1 and write nothing.
  - **File format (`SealedNote.cs`, v1):** UTF-8 JSON, at most 8 MB, read through `BoundedFile`:
    `{"v":1,"nonce":b64(12),"tag":b64(16),"text":b64,"keys":[{"id","kind":"machine"|"recovery","epk":b64(65),"nonce","tag","key":b64(32)}]}`
    with 1 to 8 keys. The text is sealed with AES-256-GCM under a fresh random note key on every save; AAD is UTF-8
    `context-note-v1\n<Name>` (the file stem, exact case). Each entry in `keys` wraps the note key for one recipient:
    ephemeral P-256 ECDH with the recipient's public key, HKDF-SHA256 (salt `epk || recipient public point`, info
    `context-note-key-v1`) to a 32-byte key, AES-256-GCM with AAD = `id`. `id` is the lower-case hex SHA-256 of the
    recipient's SubjectPublicKeyInfo (`SealedNote.KeyId`). `Seal(name, text, recipients)`,
    `Open(name, file, recipientSpki, agree)` (uses only the wrap whose `id` is that key's; `agree` is called at most
    once), `HasKey`, `AddRecipient`. Every failure is one `SealedNoteException` without a text. Key bytes are zeroed
    with `CryptographicOperations.ZeroMemory` after use. The sealed text is the note as a plain file would hold it (a
    leading U+FEFF stands for a byte order mark, line endings are kept); only valid UTF-8 notes can be marked. No
    private key, password or plain text is ever written to the data folder.
  - **Keys (`IKeyStore`):** `Supported`, `MachinePublicKey()`, `RecoveryPublicKey()` (SubjectPublicKeyInfo, `null`
    when absent; read from the OS store, never from the data folder, so a planted file can't add a recipient),
    `CreateMachineKey()`, `StoreRecoveryPublicKey(spki)`, `MachineKeyDetails()` (public data for the settings page:
    `KeyDetails` with `Name`, `Fingerprint`, `ExpiresUtc`; `null` without a machine key, never a prompt; Windows gives
    the certificate's common name, thumbprint and `NotAfter`, macOS `KeyDetails.FromPublicKey`, the upper-case hex
    SHA-256 of the public key with no expiry) and `Agree(peerPoint)` (the one private-key call; the OS may
    prompt), failing with `KeyStoreException`. `WindowsKeyStore` (`CurrentUser\My`, .NET classes only): a P-256 CNG
    key with `ExportPolicy.None`, in the TPM provider when it can agree, else in the software provider behind a
    UI-protected key, tied to the self-issued certificate `CN=Context sensitive notes`; the recovery public key is
    the certificate `CN=Context sensitive notes recovery` without a private key. `MacKeyStore` (the only file with
    P/Invoke: Security.framework and CoreFoundation): a non-extractable P-256 key from `SecKeyCreateRandomKey`, in the
    Secure Enclave with a user-presence access control when macOS allows it (an ad-hoc signed build, which is what
    ships while the release's `MACOS_SIGNING` switch is not `on`, often isn't allowed; a Developer ID signed and
    notarized build is not yet checked here), else a non-extractable login-Keychain key; `SecKeyCopyKeyExchangeResult` for `Agree`; the recovery
    public key is a generic-password item. `UnsupportedKeyStore` on every other OS (`Supported` false). `Program.cs`
    picks the store; nothing else names the real ones.
  - **Recovery file (`RecoveryFile.cs`):** `Create(password, out publicKey)` makes a new P-256 key in memory and
    returns it as PKCS#12 bytes (one shrouded key bag: PBES2, AES-256-CBC, PBKDF2-HMAC-SHA256 with 600,000
    iterations; integrity PBMAC1 with the same PBKDF2), built with `AsnWriter`; `Read(file, password)` returns the
    key (file at most 1 MB) or throws `RecoveryFileException`, also for a file with fewer than 600,000 PBKDF2
    iterations. It touches no file itself. Both normalise the password to Unicode form C first
    (`RecoveryFile.Normalized`; text without such a form is refused), so a password typed composed on one machine
    and decomposed on another gives the same key. The password needs at least 12 characters (`MinPasswordLength`),
    counted on that form; the dialog compares its two entries on it as well.
  - **Actions (`DesktopActions`, the only write path):** `SetUpSensitive(password, path)` (creates the machine key if
    missing, writes the `.pfx` with `FileMode.CreateNew` and `0600` off Windows, and only then stores the recovery
    public key; refused for a short password, a path inside the data folder or that exists, or when recovery is
    already set up), `MarkSensitive(name, confirmation)` (seals for the machine and the recovery key through a `0600`
    temp file and a no-overwrite move, then deletes the `.md`; zero `Agree` calls; refused without a confirmation, for
    `inbox`, without recovery `Set up recovery first.`, over 5 MB, and on an unsupported OS `Sensitive notes need
    macOS or Windows.`), `MakePlain(name, confirmation)`, `RestoreFromRecovery(path, password)` (reads the `.pfx`,
    creates the machine key and stores the recovery public key when missing, adds a machine wrap to every sensitive
    note the recovery key opens: `Restored <n> of <m> notes.`; a wrong password or file: Failed `Can't read that
    recovery file.`, nothing changed). `SaveNote` on a sensitive note re-seals with a new note key (zero `Agree`
    calls, same changed-on-disk check, never a `.md` or a plain temp file); `RenameNote` seals the open text under the
    new name and then removes the old file through `DeleteSensitive` (only while the note is open, else `Unlock <name>
    first.`); `DeleteNote` removes a `.ctxs` through `DeleteSensitive` (locked or open, zero `Agree` calls);
    `OpenExternally` refuses a sensitive note. Results hold fixed texts only, never a key, a password or an exception
    message.
  - **Open and lock:** `DesktopState.ReadSensitive(name)` is the only caller of `IKeyStore.Agree` for opening (once
    per open); the text then lives only in `SensitiveSession` (one open note, no key) and in the page's editor.
    `NoteEditor` (`Sensitive`) shows red `Can't unlock <name>.` when that fails. The session locks through the `Lock`
    button, `Tick()` after 5 minutes without an open, a save or editor input (`Program.cs` ticks every 15 s; the
    clock is injected) and when the window closes; leaving the note drops the text as well. On idle `Main` saves a
    dirty note first and keeps it open when that save is refused. Reopening asks the OS again.
  - **UI:** `NotesPanel` lists sensitive notes with plain ones by name, with the visible word `locked`, and has
    `Set up sensitive notes` (until recovery is set up) and `Restore from recovery file`, which open `SensitiveSetup`
    (password fields without a value, the loss warning, the OS file dialog through `IFileDialog` /
    `PhotinoFileDialog`; `The passwords don't match.`). The More actions menu of a plain note has `Mark sensitive`, of a
    sensitive note `Make plain` and `Lock` instead of `Open in default editor`; both mark and make-plain ask in a
    `ConfirmDialog` and need a saved note.
  - **Rules (tests):** no file in `src/Context.Core/` or `src/Context/` mentions `AesGcm`, `ECDiffieHellman`,
    `IKeyStore` or `SealedNote`; `DllImport`/`LibraryImport`/`extern ` appear only in `MacKeyStore.cs`, `OutlookComSource.cs` and (job 260, 282) `MacGlobalHotkey.cs`; `X509Store`
    and `CngKey` only in `WindowsKeyStore.cs`; no test references `WindowsKeyStore`, `MacKeyStore` or
    `PhotinoFileDialog` (tests use `FakeKeyStore`, which counts `Agree` and can throw, and `FakeFileDialog`), so
    `dotnet test` never touches a real key store, Keychain or file dialog; no new NuGet package.
  - **Not covered:** rollback to an older sealed file, a wrap dropped from a sealed file's `keys` by someone who can
    write the data folder (the list is under no tag; the next save writes both wraps again), plain remnants of the
    deleted `.md` (disk, backups), text in process and web-view memory while a note is open. Losing both the machine key and the recovery file (or its
    password) loses the notes. A second machine is enrolled with the recovery file; notes it creates are not readable
    on the first machine.
  - **Manual checks (no test can cover them):** on macOS and on Windows run setup, mark, open (the OS prompt
    appears), lock, restart, restore from the `.pfx`.
- **Note history** (job 174, `NoteHistory.cs`, `TextDiff.cs`, desktop only): `NoteHistory(NoteStore, utcNow)` keeps a
  note `<folder>/<Title>.md`'s versions in `<folder>/.history/<Title>/<id>.md` and a sensitive `<Title>.ctxs`'s in
  `<root>/.history/<Title>/<id>~<sealed title>.ctxs` (`<id>` = `yyyyMMdd'T'HHmmssfff'Z'[-n]` from the clock;
  `CreateNew`, `0600`; split at the first `~`, last `.ctxs` stripped). `Keep(notePath, bytes, sealedTitle?)` writes
  nothing when the bytes equal the newest version, then prunes the oldest to at most `MaxVersionsPerNote` (50) and
  `MaxBytesPerNote` (25 MB), never the new one; any failure is `NoteHistoryException`. `Versions`/`TryRead` never
  follow a link or reparse point and list only regular files with a valid name (newest first, `NoteVersion`).
  `NoteStore.SaveIfUnchanged(..., history)` keeps the previous bytes after the version check, before the move; only
  `DesktopActions.SaveNote` and the link rewrites of `DesktopActions.RenameNote` (job 206, always a version, no
  `KeptWithin` gap) pass one (capture, day's end, briefing, import and the CLI keep none). `Rename`/`Move`
  move `.history/<Title>/` along (never over an existing one; moved back when the note move fails); only `DeleteNote`
  passes `removeHistory: true` to `Delete`/`DeleteSensitive`. `List()` drops top-level dot-files. Sensitive: a save
  copies the old sealed file byte for byte (zero `Agree`); a rename seals the new file, moves the history (a clash
  removes the new file: Refused `Exists`), then `DeleteSensitive`; `MarkSensitive` seals each plain version (at most
  50, public keys only) before the note, removes the sealed ones on any failure or rollback (`Can't seal the history of
  <name>. Nothing was changed.`) and the `.md` ones on success (when that fails: Done/`PlainHistoryLeft`, red
  `Marked <name> sensitive, but its old readable versions couldn't be removed. Delete .history/<name> in the data
  folder.`); `MakePlain` (question ends `Its history will be
  removed.`) removes the sealed ones. `DesktopState.History(name)` (`HistoryView`) and `ReadVersion(name, id)` (a
  sealed version only while the note is open, one `Agree` with its `<sealed title>` as AAD) read only.
  `TextDiff.Lines(old, current)` is a Myers line diff (`\n` split, trailing `\r` dropped) bounded at
  `MaxChangedLines` (1000, else `TooDifferent`). `HistoryPanel` (the `History` menu item, a `menuitemcheckbox`, under `NoteEditor`)
  lists versions in local `yyyy-MM-dd HH:mm:ss` and the chosen one's diff (`-` red, `+` accent, unchanged grey);
  `Restore this version` asks in `ConfirmDialog` (`Restore <name> to the version from <time>? The current text is kept
  as a version.`, dirty editor `Save <name> first.`) and `DesktopActions.RestoreVersion` saves through `SaveNote`, then
  the editor reloads (`NoteEditor.ShowSavedAsync`) with accent `Restored <name> to <time>.` Known limits (README): the
  old title of a renamed sensitive note stays in its version file names; ~130-character file names can hit the Windows
  path limit; a broken `.history` blocks saves at that level until the user removes it.
- **How it runs:** two front ends over the same Core stores and the same data folder: the desktop app (a Photino
  window, see the desktop layout) and the command line (one command per process, see Command line). There is no
  terminal UI: `context` without arguments is a usage error.
- **Storage (Core):**
  - **Notes:** `NoteStore` keeps each note as a top-level `<Title>.md` file in its root folder
    (`NoteStore.DefaultFolder()` = `<ApplicationData>/Context`; the folder is created on the first
    write, never by a read). A new note's content is `# <Title>\n` (UTF-8, no BOM), written with `FileMode.CreateNew`.
    Titles are validated (`NoteStore.ValidateTitle`: no `/ \ :`, invalid file-name or control chars,
    no leading `.`, at most 100 chars, no Windows reserved name (CON, PRN, AUX, NUL, COM1-9, LPT1-9, also before a dot), no trailing `.` or space) after `NoteStore.NormalizeTitle` trims them and strips any trailing
    `.md` (ignoring case), so the title `x.md` creates `x.md`; typed note names (`note show`, `export`) are only matched against `List()` (as typed first, then with
    one `.md` stripped), never turned into a path. Note text is plain text to both front ends, never parsed as markup.
    Listing (job 275, top level, every child level and `ListSensitive`) enumerates with `MatchCasing.CaseInsensitive`,
    so `Upper.MD`, `Parent/Child.Md` and `Secret.CTXS` are listed on every OS, and leaves out every entry that is a link
    (`NoteStore.IsLink`) or has the `Directory` or `Device` attribute, so a note symlink (for example to
    `~/.ssh/id_rsa`) is never listed, found or read; a FIFO `x.md` stays listed and reads as `""`. `Read(note)` is
    `DecodeText(BoundedFile.Read(path, NoteStore.MaxNoteBytes))` (`MaxNoteBytes` = `MaxEditBytes`, 5 MB, the one note
    limit for the index, `PageTypes.Load`, `PersonAliases` and the CLI); over it, it throws `FileTooLargeException`, and
    `note show` / `export` print `Can't read <name>: it is over 5 MB.`
    `context note show` on an unreadable note answers `Can't read <name>.`
    `context export <note> <path>` writes the note's plain
    text (UTF-8, no BOM, `FileMode.CreateNew`) outside the root (`NoteStore.Contains` resolves links and compares
    ordinally; a match only when case is ignored counts when the root's file system ignores case: job 298, internal
    seams `IgnoresCase(path)` (default probe: flip one ASCII letter in the deepest existing folder, or the nearest
    folder above with a letter, and ask `SameEntry(original, flipped)`: same real path and the folder above doesn't list
    both spellings) and `SameEntry`; no letter, or an IO/access error, answers true). A note temp file
    (`NoteStore.TempPath(folder, title)`, used by `Save`, `CreateWithText` and `SaveIfUnchanged`) is
    `.{prefix}.md.{32 hex}.tmp` with the title cut to at most 64 UTF-8 bytes on a whole-rune boundary
    (`MaxTempPrefixBytes`), so a long multi-byte title stays under the 255-byte file name limit. `context import <path>` reads a
    file outside the root (at most 5 MB, never changed) and stores it as the note
    `NormalizeTitle(file name)` via `NoteStore.Save` (create, or replace an existing note through temp + move);
    the answer says to delete the source. The desktop does both through `DesktopActions` (see Import and export).
    `Rename(name, newTitle)` finds `name` through `TryFind` (never a path), normalizes and validates `newTitle` like
    `Create` (`ArgumentException` with the `ValidateTitle` text), throws `NoteExistsException` when another note has
    that title ignoring case (a case-only rename of the same note is allowed), moves the file with
    `File.Move(..., overwrite: false)` (bytes and `# Title` line unchanged; `[[links]]` elsewhere are not rewritten here; the desktop `RenameNote` does that) and
    returns the new `NoteInfo`, the note itself when the title is exactly its name (nothing written), or `null` when no
    note matches. `Delete(name)` deletes the file `TryFind(name)` found and returns true, else false (nothing deleted,
    never anything outside `List()`). There is no trash; only the desktop uses them (no command-line command).
  - **Subpages** (job 152): the children of `X.md` are the `*.md` files (extension in any case, no links, job 275) in the folder `X/` next to it (exact case
    first, then ignoring case), recursively. `NoteInfo` has `Name` (`Parent/Title`, `/` on every OS), `Title` (last
    segment) and `Parent` (null at the top level). `List()` is depth-first: siblings by title ignoring case, then
    exact, each note followed by its subpages. Never read or listed: a folder that is a symlink or reparse point (so
    no loops and no way out of the root), starts with `.`, is the top-level `calendar/`, or has no matching `.md`;
    a child whose title fails `ValidateTitle` or whose full name is over `NoteStore.MaxNameLength` (400). Below the
    top level a folder that can't be read lists as empty. `TryFind` matches against `List()` names only (paths,
    `..` and `\` find nothing). `CreateChild(parent, title)` writes `<parent folder>/<title>.md` with `# <title>\n` (the overload `CreateChild(parent, title, body)` appends `body`)
    (`CreateNew`, creating the folder); `ArgumentException` for a bad title, a missing parent, a `calendar`
    top-level ancestor or a name over 400, `NoteExistsException` for a sibling with that title. `Rename` changes only
    the last segment and moves the child folder too; `Move(name, newParent?)` (null or blank: top level) moves the
    note and its subtree, refusing a move under itself or a subpage, under `calendar` or over 400 characters. Both
    never overwrite (`File.Move` then `Directory.Move`; when the folder move fails the file is moved back and the
    exception propagates; `BeforeFolderMove` is the test hook) and check clashes against siblings (at the top level
    also sensitive names). `Descendants(name)` counts the notes below; `Delete` removes the note and every listed
    descendant, then only the child folders that are now empty. `SaveIfUnchanged` writes its temp file in the
    note's own folder, named after `Title`. `Save` (import) and `Create` stay top-level only.
  - **Templates** (job 165, `NoteTemplates.cs`): `NoteTemplates(NoteStore)`. `List()` gives the titles of the regular
    `*.md` files directly in `<root>/templates/` (job 166: the `.md` extension matched ignoring case on every OS, through
    `EnumerationOptions` with `MatchCasing.CaseInsensitive`, `AttributesToSkip = 0`, `IgnoreInaccessible = false`)
    that pass `ValidateTitle` (sorted ignoring case, then ordinal; no
    subfolders, `.ctxs`, links/reparse points or names starting with `.`; a missing, linked or unreadable folder is
    empty, nothing created). `TryRead(name, out text)` (`TemplateReadKind` `Found`/`Missing`/`Unreadable`/
    `TemplatesSensitive`) normalizes the name (`NormalizeTitle`), matches it against `List()` only (exact, then ignoring
    case; never a path), refuses when the top-level `templates` is sensitive, and reads through `BoundedFile` at
    `MaxEditBytes` (BOM dropped). `Fill(text, title, localNow)` replaces `{{date}}` (`yyyy-MM-dd`), `{{time}}` (`HH:mm`,
    invariant) and `{{title}}` in one pass (inserted literally; any other `{{...}}` kept, line endings kept, a leading
    BOM dropped). `Prepare(title, template, localNow)` returns a `TemplateNote` (`TemplateNoteKind` `Ready`,
    `InvalidTitle`, `Exists` (plain or sensitive, ignoring case; checked before the template is read),
    `TemplatesSensitive`, `Missing` with `Templates`, `Unreadable`, `TooBig` over `MaxEditBytes`); it never writes, the
    front ends write the `Ready` text through `CreateWithText` (exact bytes, no `# title` line added). Texts (both front
    ends): `No template called <name>. Templates: <a>, <b>.` / `No template called <name>. Add one as templates/<name>.md.`,
    `Can't read template <name>.`, `Template <name> is too big.`, `templates is sensitive, so its templates can't be used.`,
    plus the `CreateNote` title texts.
  - **Attachments** (job 177, `NoteAttachments.cs`, desktop only): images (PNG, JPEG, GIF, WebP) and PDFs live in
    `<level folder>/.attachments/<name>` (the folder of the note file; `List()` never lists it, it starts with `.`).
    `CleanName` is the one stored-name rule (last segment, lower-case `[a-z0-9]` extension, stem `[A-Za-z0-9_-]` with
    `-` runs collapsed, leading `-` trimmed, cut to 100, trailing `-` trimmed, `attachment` for an empty or
    `ValidateTitle`-refused stem); `IsStoredName` checks that shape on a name itself; `ClashName(stem, n)` makes
    `-2`..`-99` within 100. `Sniff` takes the type from magic bytes and requires the matching extension (no SVG/HTML).
    `Add(note, fileName, bytes)` (at most `MaxBytes` 10 MB; a note in `List()`; `.attachments` no link) writes a
    `0600` temp file in the folder, then a no-overwrite move, never touching the note or `context.db`.
    `TryResolve(note, src)` accepts only `.attachments/<stored name>`, a regular file inside the root, no link, at
    most `MaxBytes`, passing `Sniff`. `ImageSources(text)` (at most 20) and `LinkedNames(text)` (ordinal, followed
    by `)` or a space, code included) never read files; both look only `MaxScannedName` (105) characters past each
    marker and keep distinct names in a `HashSet`, so a crafted note is scanned in linear time. A note linking more
    than `MaxLinkedNames` (100) names keeps every attachment: a `MoveWithNote` to another level changes nothing and
    throws `AttachmentMoveException` (the Failed text; a note never moves without its attachments) and `RemoveUnlinked`
    removes nothing (so `KeptBySiblings` never runs names x sibling bytes unbounded). `MoveWithNote(name, newParent)` (only `DesktopActions.MoveNote`)
    copies each resolved linked file into the target level (Add rules, clash `-N`), moves the note with `Move`,
    rewrites only renamed `](.attachments/<old>` byte for byte (CRLF/BOM kept, no history version), then deletes the
    sources no remaining sibling links; any failure before that undoes everything (`NoteExistsException` and
    `ArgumentException` rethrown unchanged, else `AttachmentMoveException`; `BeforeMoveStep` `copy`/`move`/`rewrite`/
    `cleanup` is the test hook); a failed cleanup only leaves sources. `KeptBySiblings`: a sibling (same `Parent`)
    keeps a name its file text contains ignoring case; an unreadable or oversized sibling, or any sensitive note at
    the top level (never read), keeps all. `DeleteNote` reads `LinkedNamesOf` first, passes `removeAttachments: true`
    to `Delete` (each deleted child folder's `.attachments`, never through a link) and then calls `RemoveUnlinked`
    (never throws). `DesktopActions.AttachFiles(name, count, files)` (one line per file in `ActionResult.Attached`,
    more than 10: `Drop at most 10 files.`; sensitive: `Attachments can't be added to a sensitive note.`, no key-store
    call) and `OpenAttachment(name, src)` (a `TryResolve` PDF to the launcher, else `Can't open <name>.`). The page
    makes one `OnDrop(count, files)` call per drop, gets `insertAttachment` per stored file and shows images from
    `blob:` URLs built from the bytes `NoteEditor` passes to `mount` (none for a sensitive note); the bundle blocks
    window `dragover`/`drop` at load. At load it also registers one window `contextmenu` listener that calls
    `preventDefault` unless the target's `closest('input, [contenteditable="true"]')` is non-null (job 224), so app
    surfaces show no browser menu while inputs and the editor keep the text menu; `Program.cs` never uses the
    platform's context-menu switch (it would remove the text-field menu too), and `DevTools` is `true` only under
    `#if DEBUG`, so Release builds never offer Inspect or dev tools.
  - **Todos:** `TodoStore` keeps the list in `<root>/todos.json` (same root as notes; System.Text.Json,
    camelCase, UTF-8 no BOM). `List()` gives the **open** todos sorted by `Due`, then untimed before timed, then `Time`, then `Created`
    (UTC), then file order. A done todo (job 159) stays in the file with `done` (UTC date-time, an offset converted and no zone taken as UTC on read; `TodoItem.Done`, written only when set,
    so a list without done todos has the same bytes as before; absent or `null` = open; any other non-date-time value
    makes the file unreadable like a bad `due`). `ListDone()` sorts the done ones by `Done` descending, then `Created`,
    then file order. `Add`/`Migrate` keep done todos; ids and `DerivedId` cover both (`done` is not in the key). An
    older build shows done todos as open again and drops the marker on its next write (the user's choice). Writes go to
    a temp file in the root, then `File.Move(..., overwrite: true)`; a file that can't be read or parsed
    throws `TodoStoreException` and is never overwritten. Each `TodoItem` has a `Guid Id` (stored as `id`, never
    shown to the user; new ones from `Guid.NewGuid` or the injected `newId`). A todo stored without an id (files from
    before ids), or with an empty or repeated one, gets `TodoStore.DerivedId` (SHA-256 of created, due, text and its
    rank among identical todos). A **bad id** (job 151: an `id` that is not null and not a string System.Text.Json reads
    as a `Guid`, e.g. `"nope"`, `""`, `42`, `true`, `{}`, `[]`; read as a raw `JsonElement`) counts as missing and gets
    the derived id too; it never makes the file unreadable (bad `text`, `due` or `created` still do).
    Reads never write; instead `TodoStore.Migrate()` runs once at start (the desktop
    before the watcher and first refresh; the CLI commands `todos`, `todo add`, `todo done`, `current` and `review` before their
    first read) and writes the derived ids into the file when any is missing, empty or repeated (returns `true`), else
    writes nothing; a missing or unreadable file is never written and `Migrate` never throws. `Migrate(out int badIds)`
    (used by the CLI; `Migrate()` calls it) also gives the number of bad ids it replaced, 0 whenever nothing was
    written. Derivation on read stays the fallback for a file changed while the app runs, and an older build that
    drops the ids gets the same ids written again on the next start. `Complete(Guid id)`
    sets `Done` from the clock on that open todo in one read and write and returns it, or returns `null` and writes
    nothing (unknown id or already done). Done todos are never shown, reopened or pruned anywhere but the person page.
    **Recurring todos** (job 168): `TodoItem.Repeat` (`TodoRepeat?`, `TodoRepeat.cs`: `Daily`, `Weekly(DayOfWeek)`,
    `Monthly(1..31)`) is stored as the optional `repeat` string `day`, `monday`..`sunday` or `month-1`..`month-31`
    (written only when set, like `done`, so a list without recurring todos has the same bytes as before; not part of
    `DerivedId`; `Add`/`Migrate` keep it). Only those exact lower-case strings read (`TodoRepeat.TryParseStored`;
    `"Monday"`, `"week"`, `"month-01"`, `42`, `{}` make the file unreadable like a bad `done`). `TodoRepeat.Label` is the
    English marker text (`every day`, `every month`, `every Monday`). `TodoInput.TryParseRepeat(token, today, out
    repeat, out firstDue)` takes `day`, `month` (anchored on today's day of month) or an English weekday name, any case;
    first due is today, or the first date on or after today on that weekday. `TodoInput.NextDue(repeat, due, today)` is
    the first date after both `due` and `today` that fits (a month without day N uses its last day).
    `Complete(id, today, out next)` marks the todo done and, for a recurring one, appends the next open todo (same text
    and repeat, new id, `created` from the clock, `NextDue` as due) in the same write; `Complete(id)` delegates with
    `DateOnly.MinValue`. An older build ignores `repeat` and drops it on its next write. `TodoPicker.NoTodosText` and
    `NoPickText(pick, shown)` are the picker's red lines for `context todo done` and `/done`.
    **Meeting todos** (job 287, `MeetingLink.cs`): `TodoItem.Meeting` (`MeetingLink(Uid, Start, Title)`: the event's
    Uid, the occurrence's local start to the minute, its one-line title cut to 200) is stored after `notified`, only when
    set, as `"meeting":{"uid":"…","start":"yyyy-MM-ddTHH:mm","title":"…"}` (not in `DerivedId`, so a list without
    meetings has the same bytes as before). A non-object, a missing/empty/non-string `uid` or one over
    `MeetingLink.MaxUidLength` (1000), or a bad `start` makes the file unreadable; a missing `title` reads as empty.
    `MeetingLink.For(appointment)` is `null` without a Uid or over 1000 chars (so a tie never writes an unreadable file);
    `Matches` needs the same Uid (ordinal) and start minute. `Add(text, due, repeat, time, meeting)`; `Tie(id, link)`
    sets `Meeting`, `Due = link.Day` and clears `Notified` on one open todo in one read and write (keeps `Time` and
    `Repeat`), else `null` and no write. `Complete` keeps `Meeting` on the done todo, the next of a recurring todo has
    none; `MoveDue`, `MarkNotified` and `Migrate` keep it. `MeetingLink.NextOccurrence(link, day, localNow)` reads the
    days from `link.Day` (or today, when later) through `link.Day + MaxSearchDays` (100) and returns the earliest
    appointment with the Uid starting after both `link.Start` and `localNow` on the first day that has one, else `null`;
    `Missing(link, appointmentsOfDay)`. The Uid is never shown; an older build drops `meeting` on its next write.
    **Todos inside notes** (job 201, `NoteTodoParser.cs`, `NoteTodo.cs`): `NoteTodoParser.Parse(markdown)` gives a
    `ParsedNoteTodo(Line, Text, Due?, Raw)` for each line outside fences (`TagParser.NumberedLinesOutsideFences`) that is
    optional spaces/tabs, `-`/`*`/`+`, one space, `[ ]`, one space, then text; the first whitespace-delimited token
    `due:yyyy-MM-dd` (ASCII digits, a real date, not in a code span) sets `Due` and leaves the text, which is collapsed
    (`TagParser.Collapse`) and cut to `MaxTextLength` (1000, never inside a surrogate pair); an empty text is no todo;
    a topic line (`TopicParser.ParseLine` gives one, job 253) is no todo either.
    `TagIndex` stores them per plain note (`note_todos`); the note stays the only source. `NoteTodos.Load(store, index)`
    syncs and returns them; `NoteTodos.Merge(stored, notes)` gives the `TodoEntry` list both front ends show: by due date,
    on one date the `todos.json` todos first (`List()` order), then note todos by note name ignoring case, then ordinal,
    then line; undated note todos last. `TodoPicker.PickEntry(entries, query)` applies the `Pick` rules to that list.
    `NoteTodos.Complete(store, todo, history?)` (a Core write) re-reads the note (`ReadForEdit`), and only when line
    `Line` still parses to the same todo with `Raw` equal (ordinal) turns its first `[ ]` into `[x]` and saves through
    `SaveIfUnchanged` (CRLF, BOM and every other byte kept; a change in between is `Changed`); results `Done`, `Changed`,
    `Missing`, `Failed`, texts `NoteTodos.ChangedText`/`MissingText`/`FailedText(note)`. Desktop:
    `DesktopSnapshot.NoteTodos`/`NoteTodosUnreadable` (filled by `DesktopState.Build` under the index lock after the
    current view), `DesktopActions.CompleteNoteTodo(todo, confirmation)` (passes `HistoryStore`), `TodoPanel` shows the
    merged list (a note todo has no date span when undated and a `button.todo-note` with the note name that calls
    `OnOpen`, which `Main` binds to `RequestOpen`), the red `Can't read the note todos.` line for a bad `context.db`, and
    `No todos yet` only when both lists are empty and readable. Out of scope: the current view's `Due`, the review,
    person pages, the day's end, reminders, the palette's `/done`; sensitive notes are never read.
    **Timed reminders** (job 178): `TodoItem.Time` (`TimeOnly?`, stored as the optional `time` string, exactly
    `HH:mm` (`TodoStore.StoredTimeFormat`), ASCII, at most `23:59`) and `TodoItem.Notified` (`DateTime?`, UTC, stored as
    `notified` like `done`); both written after `repeat` only when set, so a file without them keeps its bytes; not
    part of `DerivedId`; any other `time` value or a non-date-time `notified` makes the file unreadable.
    `TodoInput.LooksLikeTime(token)` (1-2 ASCII digits, `:`, 2 ASCII digits) and `TryParseTime(token, out time)`
    (`0:00`-`23:59`). `DueReminders(localNow)` gives the open todos with a time, no `notified` and `Due` + `Time` <=
    `localNow`, in `List()` order (a missing file is empty, a bad one throws). `MarkNotified(ids)` stamps `notified`
    (UTC clock) on those still open and unnotified in one write and returns them in list order (none: no write, `[]`).
    `Complete` keeps both on the done todo; the next todo of a recurring one keeps `Time` without `Notified`.
    `MoveDue` keeps `Time` and clears `Notified`, so a moved todo reminds again. An older build drops both on its next
    write.
    `context todo add <yyyy-MM-dd|today|tomorrow> [HH:mm] <text>` (a time-shaped word after the date is the time; a bad
    one is the usage error `That time isn't valid. ...`), `context todo add every <weekday|day|month> <text>` and the desktop's entry row add (text at most 200 chars, past dates allowed); `context todo done <text>` picks the
    todo by its text and marks it done by its id (job 293: then appends it to the done log); the desktop marks the ticked todo done by its id.
    **No todo numbers anywhere** (command line and desktop alike): a todo is shown as date and text, picked
    by its text and marked done by its hidden id. `TodoPicker.Pick(todos, query)` (Core, used by `context
    todo done`) compares the trimmed query, ignoring case (ordinal), with each todo's one-line text
    (`TodoPicker.OneLine`: `SafeText.Clean`, line breaks as spaces) in `List()` order and returns a `TodoPick`: the
    first todo whose text equals the query, else the only one containing it, else no todo and the count of todos
    containing it (0, or 2 and more); a digit is text like any other. `todo done` checks in this order: no text (a
    usage error, `todos.json` not read), an empty list (`There are no todos to mark done.`), `No todo
    matches "<query>".`, `<n> todos match "<query>". Type more of the todo's text.` (query cleaned;
    nothing removed), then `Complete(id)`: `Done: <text>`, or `The todo list changed. Nothing was done.` when
    the todo went away in between. Job 201: it picks over the merged list (`PickEntry`; a bad `context.db` is
    `Can't read context.db.`, exit 1, before any pick); a picked note todo goes through `NoteTodos.Complete`
    (`Done: <text>`, else the `NoteTodos` texts on stderr, exit 1).
    `today` is injected as `Func<DateOnly>` (and `TodoStore` takes an optional UTC clock), so tests never
    use the real clock.
- **New features are desktop-only.** Don't add a CLI command (or a new CLI option) for a new feature unless
  its story asks for one, for example because Times or a script needs it. The commands below stay and keep
  working; a feature that changes what one of them prints or does updates it and its tests.
- **Command line (scriptable):** `context <command> [args]` runs one command through `Cli`
  (`src/Context/Cli.cs`) and exits; no arguments is the usage error `No command given. Run 'context help'.`
  (exit code 2, the data folder is not created). `Cli(stdout, stderr,
  folder, today, now, HttpMessageHandler, timesFolder)` (it takes no stdin and no environment, so no command can read
  either; `Program.cs` resolves the Times folder, see Day's end) writes plain text only (results on stdout with `\n` line ends, tab-separated
  columns; errors on stderr), no markup and no prompt; `int Run(args)` never throws.
  Commands (matched ignoring case): `help`/`--help`/`-h` (never reads the folder), `version`/`--version` (prints
  `Context <version>` from `AppVersion.Current`: the assembly's informational version without `+...`; never reads the
  folder), `capture <text...>`
  (`Inbox.Capture`: controls to spaces, trimmed, 1-1000 chars, appends `- yyyy-MM-dd HH:mm <text>` to `inbox.md` via
  `ReadForEdit` + `SaveIfUnchanged`, retries 3 times on a change on disk; a missing inbox is created as `# inbox\n` +
  the line through `NoteStore.CreateWithText`, temp file + no-overwrite move), `todos` (`<date>\t<text>` or `<date> <HH:mm>\t<text>`, no number; job 201: the merged list with note todos as `<date or ->\t<text>\t<note>`; a bad `context.db` still prints the `todos.json` todos, then `Can't read context.db.`, exit 1; `--json` adds `note` to every todo),
  `todo add <date> [HH:mm] <text...>` (`TodoInput` rules; prints `Added: <date>[ <HH:mm>] <text>`), `todo add every <weekday|day|month> <text...>` (job 168,
  `TryParseRepeat`; prints `Added: <due> <text>`; a bad unit is the usage error `That repeat isn't valid. Type: context
  todo add every <weekday|day|month> <text>.`, no text `A repeating todo needs a repeat and text. ...`),
  `todo done <text...>` (arguments joined with one space, then
  `TodoPicker.Pick`, see Todos: `Done: <text>`, plus `Next: <yyyy-MM-dd>` for a recurring todo, or exit 1 with the no-todos, no-match or several-matches line;
  no text is a usage error), `notes`, `note show <name...>`,
  `tags [tag...]` (`<tag>\t<count>` or note names), `current` (the
  current view's sections as text: `Today, <date>`, `Times` (job 277, only when `Booked`: `arrival\tHH:mm`,
  `leaving\tHH:mm` only when left, `left\t<H:mm>`, `checkpoint\t[#<n> ]<name>` only when open, each value one line;
  `Unreadable` writes `Can't read Times' day file for <date>.` to stderr, exit 1, the rest unchanged), `Appointments`,
  `Due`, `Linked notes`; no row caps, `Due` lines as
  `<date>\t<text>`; its error lines go to stderr and make the exit code 1), `review` (job 169, see Review view:
  `Review, <from> to <to>` (`yyyy-MM-dd`), then `Changed notes` (`<yyyy-MM-dd HH:mm>\t<name>`), `Done todos`
  (job 293: the done log's entries, `<done date>\t<text>`, or `No todos done.`; the heading is left out when
  `done.md` can't be read), `Overdue` (`<due>\t<text>`) and `Next week` (`<date>\t<time>\t<title> (<location>)`,
  `(no title)`), every other heading always printed; the offline line on stdout like `current`; unreadable calendar
  files, the failed feed, `Can't read todos.json.` and then `Can't read the done log.` on stderr, exit 1, the rest
  still printed; any argument is a usage
  error before the folder is read), `today` (the daily note, see Daily
  note: prints it like `note show`, creating it first when it is missing; a sensitive one is `Cli.SensitiveText`,
  exit 1; a failed write is `Can't write <name>.`, exit 1; no arguments, `--json` is a usage error),
  `search <text...>` (job 170, see Full-text search: arguments joined with one space; `<name>\t<line number>\t<line>`
  per hit, both through `OneLine`, exit 0, no hits no output; blank is the usage error `Type: context search <text>.`
  before the folder is read; too short `Type at least 3 characters to search.`, too long `Search text can be at most
  200 characters.`, bad db `Can't read context.db.`, all exit 1),
  `decisions [tag...]` (job 180, see Decision log: arguments joined with one space as the tag; per decision
  `<yyyy-MM-dd>\t<note>\t<people joined ", ">\t<text>`, each through `OneLine`, exit 0, none no output; an invalid
  tag is the usage error `Type: context decisions [tag].` before the folder is read; bad db `Can't read context.db.`,
  exit 1; `--json` is a usage error),
  `dayend [today|yyyy-MM-dd]` (job 171, see Day's end: `DayEnd.CloseDay` for today or that day, printing
  `Updated <D>.` and `Moved <n> todo(s) to <D+1>.`, exit 0; a sensitive D or D+1 note `Cli.SensitiveText`, a bad
  `todos.json` `Can't read todos.json.`, a failed write `Can't write <name>.`, all stderr only, exit 1; any other or a
  second argument, or `--json`, is a usage error before the folder is read; never reads or writes `settings.json`),
  `standup` (job 265, see Standup: prints `Standup.Compose` for today and exits 0 after the usual migrate; a bad
  `todos.json` still prints the text with its `- Can't read todos.json.` lines, writes `Can't read todos.json.` to
  stderr and exits 1; any argument, `--json` too, is the usage error `Type: context standup.` before the folder is
  read; writes nothing apart from the migrate),
  `meeting <n>` (job 167, see Meeting note: prints the note for appointment `n` of today's `current` list like
  `note show`, creating it first; no appointments is `There are no appointments today.`, a number beyond them
  `There is no appointment <n> today.`, a sensitive one `Cli.SensitiveText`, a failed write `Can't write <name>.`, all
  exit 1; a missing or non-number argument (ASCII digits, at least 1) or `--json` is a usage error; it fetches the feed
  once and never opens `context.db`),
  `new <title...> [--template <name...>]` (job 165: title words joined with one space; `--template` matched ignoring
  case; prints `Created <name>`; with a template see Templates, without one the plain `# <title>\n` note through
  `Create`; an invalid title or an existing note gives the `ValidateTitle` text or `Note <name> already exists.`, every
  refusal one stderr line, exit 1; a failed write `Can't write <name>.`; no title, `--template` without a name or
  `--json` is a usage error before the folder is read),
  `item <n> [--json] [--open]` (job 280, see Work items),
  `import <path>`, `export <note> <path>` (paths relative to the current directory, no `~`). When `Migrate(out n)`
  replaced bad todo ids, `todos`, `todo add`, `todo done`, `current` and `review` first write one extra stderr line,
  `Fixed a bad todo id in todos.json.` or `Fixed <n> bad todo ids in todos.json.` (fixed text, the bad value is never
  echoed), and otherwise behave, print and exit as on a valid file; the next run writes no such line. Exit codes: 0 success,
  1 error (one stderr line, nothing on stdout; `current` may write both), 2 usage error (one stderr line ending in
  `Run 'context help'.`; unknown commands never touch the folder). All text from disk goes through `SafeText.Clean`
  (one-line fields also `\n` to space). Tests (`CliTests`) use the
  internal constructor with a fixed zone.
  **`--json`** (job 148; exact token ignoring case, any position, repeats allowed, removed before parsing) makes
  `version`, `todos`, `notes`, `note show`, `tags`, `find`, `search`, `current`, `review` and `item` write one compact object + `\n`
  (`CliJson.cs`, `Utf8JsonWriter` with the default encoder, so `+ & ' <` and non-ASCII are `\uXXXX`): `{version}`,
  `{todos:[{due,time,text}]}` (`time` `HH:mm` or `null`), `{notes:[{name,locked}]}`, `{name,text}`, `{tags:[{tag,count}]}` or `{tag,notes:[]}`,
  `{key,value,notes:[]}` (`find`), `{query,results:[{note,line,text}]}` (`search`; `line` an int), `{today,appointments:[{start,end,allDay,
  title,location,related:[{name,tags,words}]}],calendar:{status,cachedAt,unreadableFiles},due:[{due,text,overdue}],linkedNotes:[{name,tags}],
  times:{arrival,leaving,leftMinutes,checkpoint:{workItem,name}}}` (job 277: `times` `null` for None and Unreadable, which
  has plain's stderr line and exit code; `leaving` `null` until left, `leftMinutes` an int, can be negative; absent = `null`, tags raw without `#`, times `yyyy-MM-ddTHH:mm:sszzz` in the zone; `due`/`linkedNotes` `null` when
  unreadable, `related` `null` when `context.db` can't be read),
  `{from,to,changedNotes:[{name,modified}],doneTodos:[{due,done,text}],overdue:[{due,text}],appointments:[{start,end,
  allDay,title,location}],calendar:{status,cachedAt,unreadableFiles}}` (`review`; `doneTodos` from the done log, `due` `null`
  without a due date, `null` when `done.md` is unreadable; `overdue` `null` when `todos.json` is unreadable). Names and order are a public contract (`CliJsonTests`, README). Errors stay text; with `help` or a
  write command it is a usage error before anything is read. `README.md` has PowerShell examples.
  - **Removed secret option (job 273):** an argument that starts with the removed secret option is a usage error in any position (ignoring case), checked before anything else, `--json`, `help` and `version` included: `That option was removed. Run 'context help'.`, exit 2, never echoed, the folder untouched.
- **Stack:** .NET 10 (`net10.0`), C# with nullable reference types and implicit usings on.
  `Microsoft.Data.Sqlite` 10.0.14 for `context.db`
  (referenced by `Context.Core` only; it reaches the command-line tool and the desktop through the project reference). The command-line tool has no package of its own. Tests: xUnit.
  Desktop: `Photino.Blazor` 4.0.13 (Apache-2.0; Blazor in the OS web view: WebView2 / WKWebView), `Context.Desktop` only; its tests
  use `bunit` 2.13.3 and `Jint` 4.17.0 (BSD-2, test project only; runs the editor bundle).
  `Velopack` 1.2.161 (MIT; the Windows installer and updater), `Context.Desktop` only; its version must equal the
  `vpk` tool version in `release.yml` (`ReleaseWorkflowTests` compares them), so change both together.
  Editor bundle: `src/Context.Desktop/editor/` is an npm project (exact versions in `package.json`, `package-lock.json`;
  ProseMirror + prosemirror-markdown/markdown-it, esbuild; licenses in `THIRD-PARTY-LICENSES.txt`, update it when a
  package changes). After changing `editor/src/*`, run `cd src/Context.Desktop/editor && npm install && npm run build`
  (Node from `brew install node`) and commit the rebuilt `wwwroot/editor/editor.js` and `editor.css`, and
  `wwwroot/mermaid/mermaid-frame.js` (job 263: the build's second bundle, `mermaid` 12.3.0 MIT, from
  `src/mermaid-frame.js`; `THIRD-PARTY-LICENSES.txt` has a second part for it that lists every package with bytes in
  that bundle, nested copies included, so regenerate that part when `mermaid` changes); `dotnet build`
  and `dotnet test` never need Node. Code in notes (job 283): `toggleInlineCode` (`Mod-e`, also `Mod-`` `) and
  `toggleCodeBlock` (`Mod-Alt-c`; touched paragraphs and headings become one block, a block becomes one paragraph per
  line) in `view.js`; typed (job 290): `` `text` `` becomes inline code on the closing backtick (`inlineCodeRule`: no
  empty span, none after a backtick or backslash, none over a chip or code text; the next character is plain), and
  ```` ```lang ```` + Space at a line start or Enter on a paragraph that is only ```` ```lang ```` (`enterFence`) makes a
  code block with that `params`. No text is ever escaped on save (`markdown.js` writes `node.text` as is; the parser
  keeps `\*` and `&amp;` as their source characters through `keep_escapes`, so they show raw and round-trip). The two toggles are also run by the `div.format-bar` toolbar (`EditorInterop.ToggleInlineCodeAsync` /
  `ToggleCodeBlockAsync`, shown only while mounted and not locked). Enter in a code block is a newline (also in a list
  item); the block input rules carry `inCodeMark = false`. `src/highlight.js` (DOM-free; `lowlight` 3.3.0 MIT and
  `highlight.js` 11.11.2 BSD-3, only the 11 languages of its `Languages` table, no auto-detection) gives `tokens(text,
  lang)`, drawn as class decorations by the `codeHighlight` plugin for the blocks `changedCodeBlocks` names (over
  20,000 chars stay plain). The non-Mermaid code-block view has a header with `select.code-language` (replaces the first
  params word, one undo step) and `button.code-copy` (`copyText`: clipboard, else `execCommand("copy")`). Colours are
  the `--code-*` variables in both theme blocks of `app.css`. Jint test hooks: `commandMarkdown(markdown, key[, typed[, before]])` (`before` is typed first; the key runs `editorKeys`, then `baseKeymap`)
  and `typedAtMarkdown` (`⟦ ⟧` mark the selection, a lone `⟦` the caret), `changedCodeBlocksJson`, `tokensJson`,
  `languageOptionsJson`, `withLanguage`; `EditorBundle.Settled` waits for a promise. `node_modules` is ignored and `editor/**` is excluded from the C# project.
- **Build:** `dotnet build` (must stay at 0 warnings)
- **Strong name:** (job 161) every assembly is public-signed (`PublicSign`) with the committed `Context.snk`, which
  holds the public key only; no private key exists anywhere, so no real signing happens. Its purpose is that
  `InternalsVisibleTo` names friends by name and `PublicKey`, so an assembly that only copies a friend's name gets no
  access to internals. It is not a security boundary: the key is public, anyone can public-sign with it, and .NET does
  not verify strong-name signatures. `Context.Desktop` suppresses CS8002 (Photino has no strong name); no other project does.
- **Package lock files:** every project commits its `packages.lock.json` (`RestorePackagesWithLockFile` in
  `Directory.Build.props`; direct and transitive packages with their `contentHash`; `Context.Core` and
  `Context.Desktop` list `RuntimeIdentifiers` `osx-arm64;osx-x64;win-x64`, the RIDs `release.yml` publishes, so the
  lock files hold those runtime sections too). On GitHub Actions (`CI=true`) every restore runs locked
  (`RestoreLockedMode`), so a lock file that doesn't match fails CI and the release; locally restores update them.
  A package change has to commit the updated lock files (run `dotnet restore`). `PackageLockTests.cs` checks them.
- **Test command:** `dotnet test`
- **CI:** `.github/workflows/ci.yml` (GitHub Actions, `ubuntu-latest`) runs on every push to `develop` and `main`
  and on every pull request: `dotnet restore`, `dotnet build --no-restore -warnaserror`, `dotnet test --no-build`, at the repo root.
  Read-only token, no secrets, actions pinned to commit SHAs; `CiWorkflowTests.cs` checks the file as text.
- **Release:** `.github/workflows/release.yml` (next to `ci.yml` there is no third workflow; `contents: write`; secrets only in the macOS signing step, see Signing)
  has three triggers: a push to `develop`, a merged `develop` -> `main` pull request titled exactly `release vX.Y.Z`,
  and a pushed `vX.Y.Z` tag. Four jobs: `release` (`ubuntu-latest`), then `desktop-macos` (`macos-latest`) and
  `desktop-windows` (`windows-latest`, `shell: bash`), which both `needs: release`, then `publish` (`ubuntu-latest`,
  `needs` all three). Each job has a `timeout-minutes` (`release` 30, `desktop-macos` 60, `desktop-windows` 45,
  `publish` 15). The workflow-level `concurrency` group `release` (`cancel-in-progress: false`) covers all three
  triggers, so two runs never create, upload, publish or prune at once; GitHub keeps only one pending run per group, so
  a newer push cancels an older pending run (for example a queued release-PR run): re-run the cancelled run. The
  Version step and the three `Check version` steps refuse a version, tag or target that holds a line feed or carriage
  return before anything is written to `$GITHUB_ENV`/`$GITHUB_OUTPUT`. The root `nuget.config` has `<clear />`, the
  one source `nuget.org` and a `packageSourceMapping` of `*` to it (a second source needs its own patterns). Steps of
  `release`: check out,
  set up .NET, Version, `dotnet test -c Release`,
  `dotnet pack src/Context -c Release -p:Version="$VERSION"`, smoke check, create the GitHub release (notes start
  with the output of `.github/scripts/release-notes.sh <macos>`, written to `$RUNNER_TEMP/release-notes.md`: how to
  open the desktop builds), created with `--draft`. Before the create, stale-draft deletion: the step lists the drafts
  of this exact tag (`gh api "repos/$GITHUB_REPOSITORY/releases" --paginate`, jq `select(.draft == true and .tag_name
  == env.TAG)`), checks every id is all digits, deletes each (`gh api -X DELETE`, printing `Deleted stale draft <id> of
  <tag>`) and fails before the create when the list, a check or a delete fails; drafts of other tags stay.
  - **Draft, then publish:** The release is a draft until `publish` runs: `release` creates it, the desktop jobs upload
    into the draft, and `publish` (no `if:`, so it is skipped when a needed job fails) makes it public. Its steps: check
    out, the version re-check, `Publish GitHub release`, `Prune old prereleases`. The publish step reads the release
    once (`gh release view "$TAG" --json isDraft,targetCommitish,assets`, counting only assets whose `state` is
    `uploaded`) and fails, publishing nothing, unless it is
    a draft whose `targetCommitish` is the release's commit and that holds `Context.<version>.nupkg`, both macOS zips,
    the Windows `Setup.exe`, this version's full `.nupkg` and `releases.<channel>.json`. Then the tag check: `git ls-remote` of `refs/tags/$TAG` (the token only through `gh auth
    git-credential` as the credential helper, never in argv); when the tag exists and its commit (the peeled `^{}` one
    when present) is not the draft's, it prints `Tag <tag> points at <sha>, not at <target>; nothing is published` and
    fails. A tag pushed between that check and the publish is not caught. Only then it runs
    `gh release edit "$TAG" --draft=false` (plus `--prerelease` for a prerelease version). A failed desktop job leaves
    an unpublished draft that neither users nor the updater see (re-run the failed job; `publish` then runs). This is
    what makes immutable releases work: GitHub refuses uploads into a published immutable release, so every asset is
    uploaded first. A draft has no git tag until it is published (develop and release-PR runs); GitHub then creates
    the tag on the draft's commit. Re-running the whole workflow works: the release job deletes this tag's stale draft
    first.
  - **Versions:** a `develop` push makes a prerelease `X.Y.(Z+1)-develop.<run number>`, one patch above the highest
    release tag (`.github/scripts/develop-version.sh`; `0.0.1-develop.<n>` while there is no tag). A push whose changes
    are all in the root `backlog.md` and/or `backlog/**` (the user stories) is skipped (`paths-ignore`) and makes no
    prerelease. A release PR takes its version from
    the title (`.github/scripts/release-pr-version.sh`: a plain `X.Y.Z`, no prerelease label, tag not there yet); the release and its tag are
    made on the merge commit, and closed-unmerged PRs, PRs from other branches and fork PRs never reach a step. A pushed
    tag gives its own version, must be on `main`, and a `-develop` tag is refused. The version is checked against SemVer
    in the Version step and again by the `ValidateVersion` target in `Context.csproj` (`<Version>0.1.0</Version>` there
    is only the local default).
  - **What ships:** on every release, three things. (1) The command-line tool as the .NET tool package
    `Context.<version>.nupkg` (`PackAsTool`, command `context`, written to the ignored `nupkg/` folder). The smoke
    check installs it from a throwaway nuget.config that knows only the local folder, requires
    `context --version` to print `Context <version>`, and runs `context tags` on a fresh data folder so a missing
    SQLite native library fails the release. Nothing goes to nuget.org. (2) The desktop app for macOS:
    `Context-<version>-osx-arm64.zip` and `Context-<version>-osx-x64.zip`, each a `Context.app` built by
    `.github/scripts/macos-app.sh <publish-dir> <version> <out-dir>` (Info.plist + all published files in
    `Contents/MacOS/` + `Contents/Resources/Context.icns`, refusing a publish folder without `Assets/Context.icns`; it neither signs nor zips), then either ad-hoc signed (`codesign -s -`) and zipped with
    `ditto`, or signed with a Developer ID and notarized (see Signing); the asset names are the same either way. (3) The desktop app for Windows, packed by `vpk` (`--packId ContextDesktop`, `--mainExe
    Context.Desktop.exe`): `Setup.exe`, the portable zip, this version's full `.nupkg`, a delta `.nupkg` when an
    earlier published release of the channel came down, and the feed
    files (`releases.<channel>.json`, `assets.<channel>.json`, `RELEASES*`); never a package of an older version.
    The delta base is published-only: `Pack` lists `gh release list --exclude-drafts --limit 10` (plus
    `--exclude-pre-releases` for a proper release), skips lines that are not `v`+SemVer and the run's own tag, and
    runs `gh release download <tag> --pattern "releases.<channel>.json" --pattern "ContextDesktop-*-full.nupkg"
    --dir Releases` per tag until one exits 0 with both kinds of file (else `Releases/` is emptied and the next tag
    tried). With none it warns `No earlier release downloaded; packing without a delta` and still packs; `vpk pack`
    runs without `GH_TOKEN`; no `--token` appears anywhere in the workflow.
    The desktop jobs get version, tag and commit from the `release` job's outputs (job-level `env:` `VERSION`,
    `TAG`; checkout `ref`), re-check the version against SemVer before any `dotnet` call, and upload with
    `gh release upload "$TAG" ... --clobber`, so re-running a failed desktop job works.
  - **Channels and updater:** two channels. A prerelease version is packed with `--channel develop`, a proper
    release with vpk's default channel (`win`). A copy installed by `Setup.exe` updates itself at start-up
    (`StartupUpdate`, see the desktop layout) from the channel it was installed from; the portable copy and the
    macOS zips never update. **Trust model:** the macOS app can be signed and notarized, the Windows build is unsigned,
    and the updater checks no signature (no signature check in the updater; Windows signing is a later backlog
    item). An update is trusted only as an HTTPS download from this
    repository's GitHub releases, which only the release workflow writes, so whoever can write a release here can run
    code on every installed copy. The repository URL is fixed in every installed copy: never rename, transfer or
    delete the repository or the account while installed copies exist (whoever registers the old name afterwards
    controls their update feed); ship a build with the new URL first. No token is embedded in the app; while the repository is private the
    unauthenticated updater finds nothing and stays silent. The installer cannot run in `dotnet test`: after a change
    to the desktop jobs, the next develop run is the real check.
  - **Signing (macOS only):** the switch is the repository variable `MACOS_SIGNING` (empty, `off` or `on`, exact
    case). `vars.` appears only as `vars.MACOS_SIGNING` in the job-level `env:` of `release` and `desktop-macos`;
    scripts and step `if:`s read `env.MACOS_SIGNING`. Each job reads the variable when it starts, so change it only
    between runs (else the notes can name the other kind of build). The macOS `Check version` step fails on any other value, and
    `release-notes.sh` refuses it too. Not `on` (the default): the step `Bundle, ad-hoc sign and zip`
    (`if: env.MACOS_SIGNING != 'on'`) runs as before and no secret is read. `on`: the step `Bundle, sign, notarize and
    zip` (`if: env.MACOS_SIGNING == 'on'`) runs instead; it is the only place a secret appears (its `env:`, no
    `GH_TOKEN`): `MACOS_CERTIFICATE_P12` (base64 of the Developer ID Application `.p12`),
    `MACOS_CERTIFICATE_PASSWORD`, `NOTARY_KEY_P8` (the text of the App Store Connect API key file), `NOTARY_KEY_ID`,
    `NOTARY_ISSUER_ID`. The step checks that all five are set (one stderr line with the name, never a value), then
    under `umask 077` (put back before the first bundle is built, so the shipped app keeps the ad-hoc build's file
    modes) makes a folder in `$RUNNER_TEMP` with the decoded `.p12`, the `.p8` and a new keychain with a
    random password (`security create-keychain`, `import -P` with the password in argv on the single-use runner,
    `set-key-partition-list`, `list-keychains -s`), requires exactly one `Developer ID Application:` identity and
    passes its SHA-1 hash on; a `trap` deletes the keychain and the folder on every exit. Per architecture:
    `macos-app.sh`, then `.github/scripts/macos-sign.sh <Context.app> <identity> <entitlements>` (every regular file
    under `Contents/MacOS` except the main executable, then the main executable, then the bundle, each `codesign
    --force --timestamp --options runtime --entitlements <file> -s <identity>`; never `--deep` for signing, identity
    `-` refused; then `codesign --verify --deep --strict`; exit 2 sign, 3 verify) with
    `.github/scripts/macos-entitlements.plist` (one key, `com.apple.security.cs.allow-jit`), then
    `.github/scripts/macos-notarize.sh <Context.app> <zip> <key.p8> <key-id> <issuer-id>` (`ditto` zip, `xcrun
    notarytool submit --wait` whose reported status must be `Accepted`, `xcrun stapler staple` and `validate`, `spctl
    --assess --type execute`, then the final zip again from the stapled app, moved into place last; exit 2 notarize,
    3 staple, 4 assess, 5 final zip). It fails closed: a failure prints `macOS signing failed: <stage>
    (<architecture>); nothing is uploaded` (stages `import`, `bundle`, `sign`, `verify`, `notarize`, `staple`,
    `assess`), leaves no file at that architecture's asset name, starts no later architecture, skips `Upload` and
    never falls back to ad-hoc, so the release stays an unpublished draft. The signing secrets are reachable by
    every `develop` push, as the release token is; setting the variable back to `off` (repository admins only)
    quietly ships ad-hoc builds again. Real signing, notarization and the first start under the hardened runtime
    cannot run in `dotnet test`: the first develop run with the switch `on` is the real check.
  - **Pruning:** after a proper release `vX.Y.Z` is published the `Prune old prereleases` step (`continue-on-error`;
    it runs in `publish`, after the publish step) deletes the
    `-develop` prereleases with core <= X.Y.Z and their tags (`.github/scripts/old-prereleases.sh`); proper releases
    and higher prereleases stay. With immutable releases on, GitHub may refuse the delete; the step then only warns.
  - **Rules:** actions pinned to commit SHAs (the same two as `ci.yml`, tag in the trailing comment); no `${{ }}`
    inside a `run:` script: PR fields, refs and the token reach the shell only through `env:` and the runner's own
    variables. `github.token` is the only credential of every other step than the signing step (the create, publish and prune steps, the Windows `Pack` and the two uploads). The new scripts are POSIX `sh` in the style of `macos-app.sh` (`set -eu`, usage refusal before anything is created, arguments never evaluated, no `set -x`). The three version scripts are Times' (`release-pr-version.sh` differs in one pattern: it refuses every prerelease label, Times' only `-develop`). `ReleaseWorkflowTests.cs` reads `release.yml`
    only through the small YAML reader `tests/Context.Tests/WorkflowYaml.cs` (block mappings and sequences, plain and
    quoted scalars, `|` and `>` blocks, comments; anything else throws), so reformatting the YAML does not break the
    tests, and it runs the scripts with `bash` (also the Version, `Check version` and create steps, the Windows `Pack` and `Upload` steps and the publish step, against stub `vpk`, `gh` and `git` commands, and the macOS signing step and scripts against stub `codesign`, `security`, `xcrun`, `spctl` and `ditto` that log their calls and can fail or answer `Invalid`; never a real keychain, certificate or Apple account). `/factory release --project Context` opens the release PR.
- **Run:** `dotnet run --project src/Context -- help` (command line; any `Cli` command after `--`), `dotnet run --project src/Context.Desktop` (desktop window)
- **Layout:**
  - `Context.slnx`: the solution. `Directory.Build.props` turns on `EnforceCodeStyleInBuild`, lock files and locked restore on CI,
    and public signing for all five projects (`SignAssembly`, `PublicSign`, `AssemblyOriginatorKeyFile` = `Context.snk`, `ContextPublicKey` = its hex).
  - `src/Context.Core/`: class library with the data and storage code, no UI. Its only package is
    `Microsoft.Data.Sqlite`. **No project references a terminal UI library** (`CoreDependencyTests` and
    `DesktopDependencyTests` fail on the removed console's library: assembly, package, restored graph or a mention in `src/`).
    Types stay `internal` in `namespace Context;`; `InternalsVisibleTo` grants `Context`, `Context.Tests`,
    `Context.Desktop` and `Context.Desktop.Tests`, each by name and `PublicKey` (`Key="$(ContextPublicKey)"`; every
    assembly is public-signed with the committed `Context.snk`, see **Strong name**). `Context` grants only
    `Context.Tests` and `Context.Desktop` only `Context.Desktop.Tests`, the same way; no `InternalsVisibleTo` is name-only.
    It holds the note files in `NoteStore.cs`, the todo list in `TodoStore.cs`, picking a todo by its text in `TodoPicker.cs`, todos inside notes in `NoteTodoParser.cs` and `NoteTodo.cs`, topics for the next 1:1 in `TopicParser.cs` and `MeetingTopics.cs`, review dates in `ReviewDateParser.cs` and `NoteReviews.cs`, work items in `WorkItemParser.cs`, `WorkItemView.cs` and `GitCommitLog.cs`, the desktop open request in `DesktopRequest.cs`, tag parsing in `TagParser.cs`, the shared HTML page of a note in `NoteHtml.cs`,
    `[[note link]]` parsing in `NoteLinkParser.cs`, link rewriting on rename in `NoteLinkRewriter.cs`, `@[person]` parsing and the `people` page names in `PersonParser.cs`, person aliases in `PersonAliases.cs`, `key:: value` parsing in `AttributeParser.cs`, the page types in `PageTypes.cs`, the SQLite tag and link index (with `NoteLink`, `NameLookup`) in `TagIndex.cs`, the desktop's recently opened notes (`recent.json`) in `RecentNotes.cs`, its pinned notes (`pinned.json`) in `PinnedNotes.cs`, the `.ics` parser and `Appointment` in `IcsParser.cs`, recurrence rules in `Recurrence.cs`, the calendar
    folder reader and `CalendarDay` in `CalendarStore.cs`, the secret link file in `CalendarLink.cs`, the
    download and cache in `CalendarFeed.cs`, the display sanitizer in `SafeText.cs`, bounded file reads (`BoundedFile.cs`, managed code only: a file that reports length 0 is returned empty without being opened, so a FIFO named `x.md` never blocks; an opened stream that can't seek is refused; over the cap it throws `FileTooLargeException : IOException` (`Too large.`); job 275: every full read of `.ics` files and the feed cache (`CalendarStore.ReadText`, `MaxFileBytes`), `todos.json` (`TodoStore.MaxFileBytes`, 5 MB; a directory named `todos.json` can't be read), `calendar-link.txt` (`CalendarLink.MaxFileBytes`, 4096) and notes (`NoteStore.Read`) goes through it; Core has no P/Invoke, checked by `CoreDependencyTests`) and the view data steps
    `CurrentView.cs` (the current view; meeting prep in `MeetingPrep.cs`), the daily note in `DailyNote.cs`, meeting notes in `MeetingNote.cs` (the agenda carry-over in `MeetingCarryOver.cs`), the Mail digest's rules in `MailDigest.cs`, note templates in `NoteTemplates.cs`, the done log in `DoneLog.cs` (job 293), the inbox capture in `Inbox.cs` (its triage queue, remove and snooze in `InboxTriage.cs`), the day's end in `DayEnd.cs` (Times' day file in `TimesDay.cs`, today's Times bookings in `TimesNow.cs`, `settings.json` in `SettingsStore.cs`), the morning briefing in `Briefing.cs` (Times' flexitime balance in `TimesBalance.cs`), the todo input rules (`TryParseDate`,
    `MaxTextLength`, `TryParseRepeat`, `NextDue`) in `TodoInput.cs`, the todo repeat in `TodoRepeat.cs`, waiting-for todos in `WaitingFor.cs`, `TagsView.cs` (tags), `LinksView.cs` (links), `PersonView.cs` (a person page), `FindView.cs` (`find`), `SearchView.cs` (`search`), `TagFilter.cs` (tag filters), `DecisionsView.cs` (`decisions`, with `DecisionParser.cs`), `SimilarNotes.cs` (similar notes), `BoardView.cs` (tag boards), `ReviewView.cs` (the weekly review) and `CalendarLinkView.cs`
    (the calendar link), and the command ranking for the desktop palette in `CommandMatcher.cs`; the notes list is `NoteStore.List()` itself.
  - **Data step in Core, drawing in the front end:** a view's Core data step returns plain types (a kind plus raw
    text, never markup, never cleaned or escaped, no exception messages). The front ends (`Cli`, and the desktop
    components through `DesktopState`) only map that view to their output. Cleaning (`SafeText.Clean`,
    `DesktopText.OneLine`), fixed message texts and row caps live in the front end, so each one draws the same
    data its own way.
    View records (and `DueTodo`, `LinkedNote`, `RelatedNote`) override `ToString()` to return only their type name, so their raw text
    never leaks through formatting; nobody formats a view directly.
  - `src/Context/`: the command-line tool (references `Context.Core` by `ProjectReference`, no package; packed as the
    .NET tool `context`). `Program.cs` only wires things up: whatever the arguments, it sets UTF-8 output and returns
    `new Cli(...).Run(args)` over `Console.Out`/`Error`, `NoteStore.DefaultFolder()`, the local clock and a
    `SocketsHttpHandler` with `MaxAutomaticRedirections = 5`. The commands are in `Cli.cs` and the version text in
    `AppVersion.cs`. Data and storage go to Core.
  - `src/Context.Desktop/`: the desktop app (`Microsoft.NET.Sdk.Razor`, `WinExe`, references `Context.Core`
    only, never the command-line project; components and classes in `namespace Context`). `Program.cs` only wires
    things up (stores built like `Cli`'s, Photino window `Context` 1100x750, dev tools
    only in Debug). App icon (job 284): `Assets/` holds `context-icon.svg` and the committed `Context.icns`/`.ico`/`.png` made from it by `Assets/make-icons.sh` (macOS only, never run by the build; see `Assets/README.md`); the three are content in the output and publish folders, `Context.ico` is the `ApplicationIcon` and `vpk pack --icon`, `macos-app.sh` copies `Context.icns` to `Contents/Resources/` (`CFBundleIconFile` `Context`), and `Program.cs` calls `SetIconFile` with `AppIcon.File(AppContext.BaseDirectory, OperatingSystem.IsWindows())` (`.ico` on Windows, else `.png`; null or a Photino failure keeps the default icon). `LinkRenamePlan.cs` (job 206) works out, read-only, which plain notes a rename's link rewrite
    changes (for `DesktopState.LinkUpdateCount` and `DesktopActions.RenameNote`). `DesktopState` builds a `DesktopSnapshot` (`Failed`, `Today`, `CurrentView? Current`,
    `Todos`, `Notes`, unreadable flags, `Folder`) off the UI thread with `Refresh()` (one at a time; requests during a run
    coalesce into one more run) and raises `Changed`; `Tick()` refreshes when the day changed or 5 min passed.
    `DataWatcher` (one `FileSystemWatcher` with subfolders, `IsRelevant` filter: the top-level files as before, plus
    a `.md` at any depth except under a folder starting with `.` or under the top-level `calendar/`, and the top-level
    `settings.json` (job 207: saved searches edited outside the app; any case, not in a subfolder); 300 ms
    debounce, 30 s tick timer; never creates the folder) calls them. Components (`App`, `Main`, `TodoPanel`, `NotesPanel`, `TagsPanel`, `PeoplePanel`, `LinksPanel`, `OutlinePanel`, `DayAppointmentsPanel`, `TimesTodayPanel`, `AppointmentPanel`, `PersonPanel`, `ItemPanel`, `ReviewPanel`, `TriagePanel`, `MailDigestPanel`, `ToReviewPanel`, `SimilarPanel`)
    inherit `StateComponent`, which injects the state and `DesktopActions` (internal types can't be
    public parameters). `DesktopActions` (with `ActionResult`: `ActionKind` `Done`/`Refused`/`Failed`, `ActionReason`,
    raw text) is the only write path: it never throws, runs one action at a time (`Busy`), refreshes the state after a change and has the test
    hook `BeforeWrite`. `CompleteTodo(todo, confirmation)` removes the todo with `todo.Id` through
    `TodoStore.Complete(id, today, out next)` (today from the local clock; `ActionResult.NextDue` holds the next todo's
    due date for a recurring one; a todo no longer stored: `The todo list changed. Nothing was done.`).
    `AddTodo(text, due, repeat?, time?)`. **Reminders** (job 178): `AutoReminders()` (through `Run`, no refresh) reads
    `DueReminders` with the injected local clock, calls `MarkNotified` once, then `INotifier.Show("Context",
    "<HH:mm> <one-line text>")` per marked todo, or one `Show("Context", "<n> todos are due.")` for more than
    `MaxSingleReminders` (3); a throwing `Show` is swallowed (the todo stays notified), a bad `todos.json` is Failed
    with no write and no `Show`, nothing due writes nothing. `DesktopActions` takes the notifier as its last
    constructor argument; `Program.cs` passes `SystemNotifier` (macOS: `/usr/bin/osascript` with a fixed `on run argv`
    script, title and body as argv; Windows: `powershell.exe` under `Environment.SystemDirectory`, `-NoProfile
    -NonInteractive -EncodedCommand` of a fixed script that reads the text from `CONTEXT_NOTIFY_TITLE`/`BODY` and builds
    the toast through XML text nodes; no shell, no window; text one line, at most 200 chars, never starting with `-`;
    every failure swallowed). `TodoPanel`'s entry row has `<input type="time" class="todo-time" aria-label="Due
    time">` (empty, cleared after a Done add) and a timed row a grey `span.time` after the date; `/todo <date> [HH:mm]
    <text>` in the palette (a bad time or no text: red `Type /todo <yyyy-MM-dd|today|tomorrow> [HH:mm] <text>.`).
    Note actions: `CreateNote(title)` (`NoteStore.Create`; invalid: Refused/`Invalid` with the `ValidateTitle` text;
    existing: Refused/`Exists` `Note <name> already exists.`; else Failed `Can't create that note. Nothing was done.`),
    `RenameNote(name, newTitle, confirmation)` (`NoteStore.Rename`; same name: Done/`Unchanged`, no refresh; same
    invalid/exists texts; gone: Refused/`Missing` `<name> no longer exists. Nothing was done.`; job 206: `LinkRenamePlan`
    first finds the plain notes whose links `NoteLinkRewriter` would change, a failed round trip is Refused/`Invalid`
    `Links to <old> can't be written with the name <new>. Nothing was renamed.`, and with notes to change and no
    confirmation Refused/`NotConfirmed`, both writing nothing; then the note is renamed and each note is read, rewritten
    and saved through `SaveIfUnchanged(..., HistoryStore)` (a version always kept), up to 3 tries on changed on disk; Done
    `Renamed to <new>.`, `Renamed to <new>. Updated links in <n> notes.` or Done/`LinksLeft` `Renamed to <new>, but links
    in <k> notes couldn't be updated: <up to 3 names>[ and <m> more].`; `ActionResult.Name` is the new full name; one
    refresh. `DesktopState.LinkUpdateCount(name, newTitle)` gives that count read-only (0 for same, case-only, invalid
    or taken names); Main asks `Rename <old> to <new>? Links in <n> notes will be updated.` (`Rename`, focus on Cancel;
    Cancel/Esc keeps the name field) when it is above 0), `DeleteNote(name, confirmation)` (no
    confirmation: `NotConfirmed`; `NoteStore.Delete`, the whole subtree), `CreateSubpage(parent, title)`
    (`NoteStore.CreateChild`; Done text is the full name; the `CreateNote` texts, a missing parent `<parent> no longer
    exists. Nothing was done.`, too long `A note's full name can be at most 400 characters.`, below `calendar`
    `<parent> can't have subpages. Nothing was done.`), `CreateLinkedNote(target)` (job 233: the note part of
    `NoteLinkParser.Split(target)`, trimmed, one trailing `.md` dropped, split at `/`, each segment trimmed and normalized;
    every segment through `ValidateTitle` and the 400-character full name, else Refused/`Invalid` with that text; a
    top-level `calendar` parent Refused/`Invalid` and a sensitive top-level parent Refused/`Locked`, both
    `<parent> can't have subpages. Nothing was done.`, checked before any write, no key-store call; an existing note,
    any case, Done/`Unchanged` with its name; else each missing parent through `Create`/`CreateChild` (`# <title>\n`,
    existing ones reused with their spelling) and the note; a failed write Failed `Can't create that note. Nothing was
    done.`, parents made before it stay; Done text is the full name), `MoveNote(name, newParent)` (`NoteStore.Move`; the parent is
    matched through `TryFind` only, unknown: Refused/`Missing` `No note called <parent>. Nothing was moved.`; under
    itself: `A note can't move under itself or one of its subpages. Nothing was moved.`; already there:
    Done/`Unchanged`; Done text is the new full name) and `OpenExternally(name)`, which never writes or refreshes: it
    resolves `name` through `TryFind` and calls `INoteLauncher.Launch(path)` once, only when the path is `.md`, inside
    the root (`NoteStore.Contains`) and a regular file (no symlink/reparse point, seekable; an empty file is refused off
    Windows because it can't be told from a FIFO), else Refused `Can't open <name> in another app.` (Failed with the
    same text when the launcher throws). `DesktopActions` takes the launcher in its constructor; `Program.cs` passes
    `ShellNoteLauncher` (`Process.Start` with `UseShellExecute = true`, the path as `FileName`, no arguments). `ConfirmDialog` (in-window
    `role="dialog"` overlay, focus on `Cancel`, Esc cancels, `Busy` disables it) is the only creator of a
    `Confirmation` (public sealed, internal constructor), which destructive actions require. The desktop shows no todo
    numbers (not in `TodoPanel`, not in the `Due` table): `TodoPanel` shows each todo as a `<label>` line with a
    checkbox, date and text (a person mention in the text is a `mention` button, job 159); ticking it opens the dialog (the box stays ticked while it is open), and a
    `role="status"` result line shows `Done: <text>` (accent), `Done: <text>. Next due yyyy-MM-dd.` for a recurring
    todo, or the fixed red text. A recurring todo has a grey `span.repeat` after its text (`TodoRepeat.Label`, same
    `title`). `TodoPanel.AskDone(todo)` opens the same dialog for the palette's `/done`.
    **Editing:** clicking a note name (`NotesPanel` button), a link or `/open` in the palette shows `NoteEditor` in the
    main region (`Main` keeps the open name; a dirty editor saves first, job 203, and asks `Discard changes to <name>?`
    only when that save failed). `NoteEditor`
    reads through `DesktopState.ReadForEdit` (`NoteStore.ReadForEdit` -> `EditableNote`: text with `\n`, raw-byte
    SHA-256 `Version`, CRLF and BOM flags; job 271: `HadCrlf` is the most common ending (a tie goes to the first
    break), `LineCrlf` one flag per `\n` for a mixed file, `null` when uniform; `LineEndings.Read`/`Apply` is the one
    place for endings, used by `NoteStore` and `SensitiveSession`: lines `TextDiff.Lines` marks `Same` keep their
    ending, added lines and a `TooDifferent` diff get the majority), mounts the page editor through `EditorInterop`
    (the only class that touches
    `IJSRuntime`; `[JSInvokable]` `OnTag`/`OnLink`/`OnPerson`/`OnAttachment` only navigate, `OnDrop` only raises `FilesDropped`, `OnChanged` only raises `Changed`) and saves through
    `DesktopActions.SaveNote` -> `NoteStore.SaveIfUnchanged` (unchanged text writes nothing; version re-checked before
    the move; per-line endings and BOM kept, `saved` has the flags of the bytes written; temp file mode `0600`).
    **Auto save** (job 203): `view.js` calls `OnChanged` on every
    `docChanged` transaction; each `Changed` restarts the pause (`IAutoSavePause`, real `AutoSavePause.Pause` = 1 s,
    tests `FakeAutoSavePause.Elapse()`), and when it elapses a dirty editor saves. `NoteEditor.SaveAsync(bool auto)` is
    the one save path, serialized by a `SemaphoreSlim` (cancels the pending pause, waits for a running save): grey
    `Saving…`, then grey `Saved.` (auto) or accent `Saved <name>.` (Ctrl/Cmd+S, `/save`, `LockOpen`); a
    refusal or failure shows its red text, keeps the text (no remount) and starts no new save without a new edit; an
    auto save refused as `Busy` restores the previous status and waits for the next pause. After `Done` the editor is
    marked clean only through `ContextEditor.markCleanIf(element, markdown)` (compare and mark in one JS turn), so text
    typed during a save stays dirty and saves after the next pause. The pause is cancelled on reload, `ShowSavedAsync`,
    an explicit save and dispose; an unedited note is never saved. `SaveFirstAsync()` (true when nothing unsaved is
    left) is what `Main` calls before leaving the note (`KeptUnsaved`: `RequestOpen`, mentions, `/today`, `/briefing`,
    `/meeting`) and before every `Save <name> first.` refusal; the source rule that only bound handlers call a method
    that calls `Actions` exempts exactly `SaveFirstAsync`/`AutoSaveAsync` -> `SaveAsync`, and `SaveAsync` counts as
    bound only through the bound key handler `Save` (Ctrl/Cmd+S). Window close: `Program`'s
    closing handler asks the singleton `CloseGuard` (wired by `Main` through `Attach`/`Detach`, `HasUnsaved` read
    without the dispatcher): with unsaved changes it cancels the close, saves first asynchronously and closes once
    through the window thread (`PhotinoWindow.Invoke`), or shows `Discard changes to <name>?` (Discard closes, Cancel
    keeps the window). `DesktopActions.SaveNote(opened, text, auto)`: an auto save keeps a history version only when the
    note has none or its newest is at least `AutoVersionGap` (5 min, history clock; `NoteHistory.KeptWithin`). While the page's editor differs from the saved text, a
    `span.dirty-dot` (`role="img"`, `Unsaved changes`) shows right after the note title: the page calls
    `EditorInterop.OnDirty(bool)` only when that state flips (`DirtyChanged`), a `Done` save and a reload clear it, and
    it is display only (every decision still asks `IsDirtyAsync`). Tag and note-link chips are tab stops (`chipDom` in
    `schema.js`: `tabindex="0"`, role `button` / `link`, a `:focus-visible` ring in `editor.css`), and a bare Enter on a
    focused chip (`opensChip` in `view.js`) calls the same `OnTag` / `OnLink` as a click; every other key on a chip
    reaches no keymap. Open and save both stop at `NoteStore.MaxEditBytes` (5 MB of plain
    file bytes, read through `BoundedFile`): a bigger or non-seekable note can't be opened (`Can't read <name>.`), a save
    that would be bigger is refused, and a file that grew past it counts as changed on disk. New components are styled
    in `app.css`; `editor.css` holds only `.note-editor-body` and ProseMirror's own classes. `TagPanel` lists `DesktopState.NotesWithTag`. `CommandPalette` (Ctrl/Cmd+K) ranks
    `/board`, `/briefing`, `/capture`, `/decisions`, `/done`, `/donelog`, `/export`, `/find`, `/import`, `/meeting`, `/new`, `/open`, `/review`, `/save`, `/search`, `/settings`, `/share`, `/similar`, `/standup`, `/tags`, `/today`, `/todo`, `/triage` (no `/current` since job 216) with `CommandMatcher` (in Core; ties
    in Levenshtein score are ordered by `SwapDistance`, which counts an adjacent swap as one edit, so `/opne` runs `/open`).
    `/todo every <weekday|day|month> <text>` (job 168) adds a recurring todo (`TryParseRepeat`, then `AddTodo`), accent
    `Added: <text>`; a missing or bad unit or missing text is red `Type /todo every <weekday|day|month> <text>.`; a bad
    date stays red `Type /todo <yyyy-MM-dd|today|tomorrow> <text>.`. `/done <text>` picks among `Snapshot.Todos` with
    `TodoPicker.Pick` (blank: red `Type /done <text>.`; unreadable list: `TodosUnreadableText`; then the CLI's red
    texts) and opens `TodoPanel`'s `Mark this todo done? <text>` dialog; nothing is written before Confirm.
    `/new <title> [--template <name>]` (job 165): the first whitespace-separated `--template` token (ignoring case)
    splits the trimmed title from the trimmed template name and calls `DesktopActions.CreateNoteFromTemplate(title,
    template)` (`NoteTemplates.Prepare` with the injected local clock, then `CreateWithText`; the Templates texts, no
    key-store call); Done opens the note through `RequestOpen` with accent `Created <name>`, else the red text; a blank
    title or template is red `Type /new <title> --template <template>.`; without the token `/new` is unchanged.
    `/today` (argument ignored; the top bar's `Today` button runs the same `Main.ShowToday`, job 216) asks `Discard changes to <name>?` first when another note has unsaved changes (not
    when the open note is today's note itself: the editor stays as it is, unsaved text included), then
    calls `DesktopActions.OpenDailyNote()` and opens the note (a sensitive one through the unlock flow), with the
    accent `Created <name>` after a create and the red failure text otherwise. With today's note already open it keeps
    the editor and only closes a shown page (Triage, People, Tags, Settings, Appointment).
    `/meeting <number>` (job 167) picks the appointment from the shown `Snapshot.Current` (numbered as `context current`
    numbers them; the desktop no longer shows the numbers, job 216) and works like `/today` through
    `DesktopActions.OpenMeetingNote(appointment)`; a blank or invalid number is red `Type /meeting <number>.`, no
    appointments or a number beyond them the CLI's texts in red, a failed write red `Can't write <name>.`.
    `/briefing` (job 172, hint `Open today's briefing`, argument ignored) works like `/today` through
    `DesktopActions.OpenBriefing()` (`BriefingName()` for the unsaved-changes check): accent `Created <name>` after a
    create, red `briefings is sensitive, so no briefing can be written.` (`BriefingSensitiveText`) or
    `Can't write <name>.` (`BriefingFailedText`).
    **Meeting note:** `MeetingNote` (Core) names the note `Meetings/<title part>/yyyy-MM-dd` (job 220; local start
    date, invariant). `TitlePartFor`: the one-line title with `/ \ : * ? " < > |` and control characters as spaces,
    collapsed, trimmed, cut to `MaxTitlePart` (100) without splitting a surrogate pair, trailing `.md`, dots and spaces
    stripped, then leading dots and spaces; a part `ValidateTitle` still refuses (a Windows-reserved stem) becomes
    `Meeting <part>` (cut and trimmed again), an empty one `Meeting`; always valid and equal to `NormalizeTitle`.
    `LegacyNameFor` keeps the old top-level `yyyy-MM-dd <title>` rule (89-character cut, leading dots kept);
    `IsNoteOf(appointment, name)` is true for either name, ignoring case (the desktop's unsaved-changes checks for
    `/meeting` and `Create a new note`). `Format` writes `# yyyy-MM-dd`, `#meeting`, `- Title:`, `- Time:`
    (`MeetingNote.Time`), `- Location:` (only when set) and `- Attendees:` (`@[name]` mentions, or `none`).
    `Ensure(notes, appointment)` returns `Existing` for an existing new-style note (any case), else `Existing` or
    `Sensitive` for a top-level legacy note, all without writing; else it creates the missing `Meetings`
    (`# Meetings\n`) and `Meetings/<part>` (`# <part>\n`) through `Create`/`CreateChild` (existing ones in any case are
    reused, their spelling kept) and the note through `CreateChild` (a race counts as existing). A sensitive
    top-level `Meetings` throws a plain `IOException` (the `Can't write <name>.` line). Existing legacy notes are
    never moved. **Carry-over** (job 245, `MeetingCarryOver.cs`): only when `Ensure` creates the note, it appends
    `MeetingCarryOver.Section` for the previous occurrence: `Previous(notes, meetingPage, date)` is the latest direct
    subpage of the same `Meetings/<part>` page (any case) titled exactly `yyyy-MM-dd` (a real date) before the day;
    `Extract(markdown)` gives, in line order, every open `NoteTodoParser` todo (`Raw`, leading spaces/tabs removed)
    and the lines of every ATX `Open points` section (ignoring case, outside fences, up to the next heading of the same
    or higher level outside fences; copied as written without its heading, outer blank lines dropped; a todo in it once),
    `\r` dropped; `Section(name, lines)` writes `\n## From last time\n\nFrom [[<name>]]:\n\n` (plain `From <name>:`
    for a name with `[`, `]`, `#` or a backtick) and at most `MaxLines` (200) lines, then `- … and <k> more in the
    previous note.`. The previous note is read through `BoundedFile` at `MaxPreviousBytes` (1 MiB), never written; too
    large, unreadable (`IOException`, `UnauthorizedAccessException`) or nothing open: no section. `Ensure`'s
    `beforeReadPrevious` is the test hook. **Topics** (job 253): `Ensure(..., topics)` writes, only when it creates the
    note and the list is non-empty, `TopicsSection(topics)` before the carry-over: `\n## Topics\n\n`, then
    `- @[<Person>]: <OneLine text> (from [[<note>]])` per topic (plain `(from <note>)` for a name with `[`, `]`, `#` or
    a backtick), at most `MeetingCarryOver.MaxLines` (200), then `- … and <k> more.`; null or empty: today's bytes. `Today(calendar, feed, day)` gives the numbered list (`CurrentView` order: files plus feed).
    **Daily note:** `DailyNote` (Core) names the note `yyyy-MM-dd` (invariant) for the local day. `Ensure(notes, day,
    compose)` returns `Existing` or `Sensitive` for a note of that name without calling `compose` or writing, else
    creates the top-level note through `CreateWithText` (a `NoteExistsException` race counts as existing).
    `Compose` writes `# <name>`, `## Appointments` (`CurrentView.Time` lines, `(no title)`, ` (<location>)`,
    `- No appointments.`, unreadable calendar files and a failed feed as lines) and `## Todos` (open todos due on or
    before the day, overdue ones with ` (due yyyy-MM-dd)`, `- Nothing due.` or `- Can't read todos.json.`); every
    inserted text is one line; it shares `Fetch`, `FeedEvents` and `DueOn` with `CurrentView` and never opens
    `context.db`. An existing daily note is never read, changed or replaced (except by the day's end below).
    **Day's end** (job 171, Core `TimesDay.cs`, `DayEnd.cs`, `SettingsStore.cs`): `TimesFolder.Resolve(value)` gives
    the Times app's data folder from `TIMES_DATA_DIR` (absolute, no whitespace at either end; `null`/empty:
    `<ApplicationData>/Times`; anything else `null`). `TimesDay.Read(folder, day)` reads `<folder>/yyyy-MM-dd.json`
    read-only through `BoundedFile` (64 KiB, BOM dropped, duplicate properties refused; never throws): `Missing`,
    `Unreadable`, `Absence` or `Entries` (Times' entries format or the legacy `arrival`/`leaving` one) with `Arrival`,
    `Leaving`, `HasLeft`, `WorkTime`, `IsWorking`, `WorkTimeAt(now)` (job 277) and `Groups` (per work item or name, in first-booked order). Nothing ever writes
    into the Times folder. `DayEnd(notes, todos, calendar, feed, timesFolder, settings?)`: `RefreshWorked(D)` replaces
    or adds D's `## Worked` section (`- Arrived HH:mm, left HH:mm, worked h:mm`, a missing arrival `--:--`, one line
    per group; or the missing/unreadable/absent/not-left line), creating the daily note through `DailyNote.Ensure`;
    `RefreshStandup(D, zone)` (job 265) replaces or adds D's `## Standup` section (`## Standup\n\n` + `Standup.Compose`)
    the same way (never for a sensitive D). **Standup:** `Standup.Compose(todos, calendar, feed, timesFolder, today,
    zone[, out todosUnreadable])` (Core, read only) gives `Yesterday (<last workday>)` (`Standup.LastWorkday`: the
    latest of today-1..today-7 with Times entries, an unreadable file met first, else the latest Monday to Friday
    with `- No Times bookings in the last 7 days.`; the groups as `DayEnd.FormatGroup`, then `- Done: <text>` for todos
    done (local date in zone) from that day to yesterday, oldest first; else `- Nothing logged.`), `Today` (the daily
    note's appointment lines via `DailyNote.AppendAppointmentLines` without `- No appointments.`, then the due
    non-waiting todos as in `## Todos`; else `- Nothing planned.`) and `Blocked` (every open waiting-for todo, `- Person:
    Rest (follow up yyyy-MM-dd)`, `- Person (follow up …)` when there is no rest; else `- Nothing blocked.`), blank lines
    between blocks; a bad `todos.json` puts `- Can't read todos.json.` in place of each block's todo lines.
    `MoveTodos(D)` calls `TodoStore.MoveDue(D, D+1)` (open todos due on or before D get due D+1, id/created/repeat
    kept, one write, none when nothing is due) and appends `- <text>` lines to `## Moved to tomorrow` in D's note and
    `## Moved from <D>` in D+1's (created after the move); `CloseDay(D)` does both. Every edit is `ReadForEdit` +
    `SaveIfUnchanged` with 3 retries; a sensitive D (or D+1 for the move) is never read or written.
    `AutoClose(today, now)` (only with a `SettingsStore`): `<root>/settings.json` `{"dayEnd":{"worked":
    "yyyy-MM-ddTHH:mm","moved":"yyyy-MM-dd"}}` (other properties kept, temp file + move, a broken file is never
    overwritten and stops the run) records each step before it runs: catch-up for yesterday (left, not yet moved),
    today's Worked refresh once per new leaving that is not later than `now`, and today's move once at a leaving at or
    after `EveningCutoff` (17:00) when tomorrow is not sensitive. Manual runs (`CloseDay`, the CLI, the button) never
    touch `settings.json`. **Saved searches** (job 207) live in the same file: `"savedSearches":[{"name":"…","query":"…"}]`
    in saved order (`SettingsStore.ReadSavedSearches`/`SaveSavedSearches`, `SavedSearch(Name, Query)`; rules in
    `SavedSearches`: name trimmed with whitespace runs collapsed, 1 to `MaxNameLength` (50) chars, no control
    or format characters (bidi marks, zero-width) or lone surrogates, unique ignoring case; query a valid tag filter of at most 200 chars or a full-text text of 3 to 200;
    at most `MaxCount` (50)). A non-array, an item that isn't an object with exactly string `name` and `query`, or a
    broken rule throws `SettingsStoreException`; a saved-search write re-checks `dayEnd` and `savedSearches` and never
    overwrites a broken file. Every write uses relaxed JSON escaping (non-ASCII as is) and throws
    `SettingsTooLargeException` (an `IOException`), writing nothing, when the file would be over 64 KiB. `ReadDayEnd`/`SaveDayEnd` ignore and keep `savedSearches`. `DesktopActions.SaveSearch(name,
    query)` (texts `Type a name for the search.`, `A search name can be at most 50 characters, without control
    characters.`, `Can't save that search.`, `A saved search named <name> already exists.`, `Can't save more than 50
    searches.`, `settings.json would be over 64 KiB. Nothing was saved.`, Failed `Can't read settings.json. Nothing was saved.`, Done `Saved search <name>.`) and
    `RemoveSavedSearch(name)` (ignoring case; a name that can't be normalized is gone; Refused/`Missing` `That saved search is gone. Nothing was done.`, Done
    `Removed saved search <name>.`) are the only writers. End the day (`end-day`) is the first item of the More actions menu while today's daily note is open (job 216, plain
    and sensitive alike; job 200 had it on the dashboard). `Main.RunPageAction("end-day")` saves unsaved editor text
    first (a failed save is red `Save <name> first.` and nothing is written), calls
    `DesktopActions.EndDay(Snapshot.Today)`, shows the result in `p.main-status` (grey on Done/Unchanged, `Day closed.
    Moved <n> todo(s) to <D+1>.`, else red) and then reloads the editor (`NoteEditor.ReloadAsync`) so the new
    `## Worked` and `## Moved to tomorrow` sections show and a later save is not refused as changed on disk; Standup
    (`standup`, job 265) is the next item on today's note and works the same way through
    `DesktopActions.WriteStandup(Snapshot.Today)` (grey `Standup written to <D>.`, red `<D> is sensitive, so no standup
    can be written.` or `Can't write <name>.`); `/standup` (hint `Write today's standup`, argument ignored) runs it
    on today's note, opening it first (from another note it writes, then opens today's note); `Program.cs` calls `DesktopActions.AutoDayEnd()`
    at start and every `DayEndInterval` (30 s) from its own timer (Busy: Refused, skipped), followed by `AutoBriefing()`.
    **Corrections and balance** (job 172): `TimesDay.Corrections` reads Times' `"corrections":[{"minutes":<int>,
    "reason":<string>}]` (exactly those two properties; minutes never 0, at most `MaxCorrectionMinutes` 59999 either
    way) for entries and absence files; any other shape makes the file `Unreadable`. `TimesBalance.Compute(folder,
    through)` lists only the folder's top-level files named exactly `yyyy-MM-dd.json` (a real date) on or before
    `through` and reads each through `TimesDay.Read`: an arrival day adds `WorkTime - target`, an absence day 0, every
    readable day its corrections; `Counted` is the days with an arrival, an absence or a correction, `Skipped` the
    unreadable files; `NoFolder` (null or missing) and `Unlistable` (listing threw). The target is
    `{"settings":{"daily-target":N}}` from `<folder>/settings.json` (`ReadTarget`, `BoundedFile` 64 KiB, 0 to 1440,
    duplicates refused), else 480. Nothing ever writes into the Times folder.
    **Morning briefing** (job 172, Core `Briefing.cs`): the stored page `briefings/yyyy-MM-dd`, a subpage of the
    top-level `briefings` note (`# briefings\n`, created when missing). `Compose(todos, calendar, feed, timesFolder,
    today, reviews)` writes `# yyyy-MM-dd`, the daily note's `## Appointments` and `## Todos` byte for byte
    (`DailyNote.AppendSections`), `## Follow-ups` (job 243, see Waiting for), `## To review` (job 256, see Review dates), `## Open checkpoint` (one `- <date>: #<n> <names>` / `- <date>: <name>` line per
    open group of the latest day from today-1 back to today-7 with at least one entry; else `- None open on <date>.`,
    `- No Times bookings in the last 7 days.` or `- Can't read Times' day file for <date>.` for the first unreadable
    file met) and `## Flexitime balance` (`- <+|->H:mm through <today-1>.`, `+0:00` for zero, plus `- Skipped <n>
    Times day file(s) that can't be read.`; else `- No Times bookings.` or `- Can't read Times' folder.`); every Times
    name is one line through `SafeText.Clean`. `Ensure(notes, day, compose)`: a sensitive `briefings` is `Sensitive`
    (nothing read or written), an existing page `Existing` without calling `compose`, else `CreateChild` (a race counts
    as existing); an existing page or `briefings` note is never changed. `ShouldCreate(today, timesToday, atStart)`:
    today's Times file has an arrival, or `atStart` on Monday to Friday and today is no absence day.
    `DesktopActions.AutoBriefing()` (the first call that runs is the start) creates the page once per day when
    `ShouldCreate` holds and then offers it through `DesktopState.OfferBriefing(name)`; an existing page is only
    remembered, never offered. `Main` takes the offer (`State.TakeBriefing()`, in `OnInitialized` and after every
    state change) and opens the page when no note has unsaved changes and no dialog or edit field is open; otherwise the
    editor is left alone and the accent status reads `Today's briefing is ready: briefings/<day>. Type /briefing to open
    it.`. The page is a snapshot, never refreshed.
    **Pages (job 193):** `Main` holds one `page` (none, triage, people, tags, settings, appointment, board, search); opening one replaces another. The note area
    (`div.note-area`: breadcrumb to `LinksPanel`, or with no note open only the grey `p.no-note` `No note open. Click
    Today to open today's note.`, job 216) gets `hidden` while a page shows, but stays mounted so
    unsaved editor text survives. Each page's root has class `page` and an icon-only `button.close` (aria-label and title
    `Close`) last in `.panel-bar`; `TagPanel` keeps its text `Close`.
    **Settings:** `SettingsPanel` (the `Settings` button in `NotesPanel`, or `/settings`; `Main`'s `page`;
    a page in place of the note area; the × (job 193), opening a note or `Today` hides it) has these sections (the *Repository folders* one is job 280's, see Work items), all
    reloaded after every `State.Changed`. *Exchange calendar* shows `DesktopState.CalendarLinkSetting()`
    (`CalendarLinkView.Show`: grey `No calendar link set.`, the masked link with the last download in the calendar's
    zone, or red `Can't read calendar-link.txt. …`), one `<input type="password">` (no `value`, no `@bind`,
    `maxlength` `DesktopActions.MaxCalendarLinkLength` = 2048; rebuilt with a new `@key` after a save so the typed
    link leaves the DOM, and the component's `typed` copy is cleared on save and on `Close`), `Save link` / Enter
    (`DesktopActions.SetCalendarLink(value, Confirmation?)`: length and `CalendarLink.Validate` first, so `off` is
    Refused and never removes the link; with a link stored or unreadable and no confirmation it is Refused
    `NotConfirmed` and the panel asks in a `ConfirmDialog`) and `Remove link`
    (`DesktopActions.RemoveCalendarLink(Confirmation)`); both go through `CalendarLinkView.Run`, delete the cached
    calendar copy and answer with fixed texts and the mask only. *Sensitive notes* shows `DesktopState.SensitiveKey()`
    (`SensitiveKeyView`: `Unsupported`, `NotSetUp`, `Unreadable` or `Shown` with `KeyDetails`): rows `Name`,
    `Thumbprint` (whole, selectable) and `Expires` (`yyyy-MM-dd` in UTC or `Does not expire`); it never calls
    `Agree` and creates no key. Both reads run off the UI thread and never throw.
    **Review:** `ReviewPanel` (`/review`, argument ignored; `Main`'s `reviewShown`, at the top of the main region
    under the top bar) draws `DesktopState.Review()` (off the UI thread, never throws: on an error an empty view
    with `TodosUnreadable` and `DoneLogUnreadable`): heading `Week review`, `Close`, the grey range, then `Changed notes` (date and a
    `note-button` through `RequestOpen`), `Done todos` (job 293: the done-log entries, date and text), `Overdue` (`overdue` dates), `Next week` (an `h4` per day with
    appointments: time, title, location), the grey empty texts (`No notes changed.`, `No todos done.`,
    `Nothing overdue.`, `No appointments next week.`), red `Can't read the done log.` in Done todos, red `Can't read todos.json.` in Overdue and the
    calendar texts (as `DayAppointmentsPanel`'s feed line). It reloads on `State.Changed` keeping the shown rows;
    `Close`, opening a note or `Today` hides it.
    **Board (job 242):** `BoardPanel` (`section.page.board-panel`, `/board <tag>` or `TagPanel`'s `button.board-open`;
    `Main`'s `page`, `Page.Board`, `boardShown`; a bad argument is red `Type /board <tag>.` in the status line) has the
    heading `Board #tag` and the icon-only ×, and shows `DesktopState.Board(tag)` (`BoardPage`: settings read, then
    `BoardView.Build` under the index lock): one `section.board-column` per column (`aria-label` and `h3` its name, `No
    status` for cards without a token) with a `ul` of `li.board-card[draggable=true]`: the note's name as a
    `button.note-button` (`RequestOpen`), a todo card's text in `span.board-todo`, and `button.board-move` ← / →
    (`Move to <col>`, the columns on either side; a No status card only to the first). A drop on another column or a
    move button calls `DesktopActions.MoveBoardCard` (`BoardView.Move`): Done reloads; Changed, Missing, Failed and
    Busy show their red line in `p.board-result`. Grey `No board for #tag. Add it under "boards" in settings.json.`,
    red `Can't read settings.json.`, red `Can't read the board.`, grey `No notes or todos have #tag.`. It reloads on
    `State.Changed`, keeping the shown cards.
    **People (job 239):** `PeoplePanel` (`section.page.people-panel`, the person icon `button.people-open` in `NotesPanel`'s
    bar between Triage and Tags, `OnPeople`; `Main`'s `page`, `Page.People`) lists the person pages (`People.PersonOf`)
    of `Snapshot.Notes` only (no index, no file read), sorted by `Title` ignoring case, then ordinal, in
    `ul.people-list` (`aria-label="People"`): one `button.person-open.cut` per page, text `DesktopText.NoteName(Title)`,
    `title` through `OneLine`, opening it through `RequestOpen`; none: grey `p[role=status]` `No people yet. Mention
    someone as @[Name] and open the mention to add their page.`. Sensitive notes are not listed. Its × closes it.
    **Filters** (job 291, Core `PeopleAttributes.cs`): `PeopleAttributes.Build(index, store)` (`Found`,
    `IndexUnreadable`) syncs and reads `AttributeValues("affiliation"/"role")` of the direct person pages only: `People`
    (note name -> `PersonAttributes`: shown `Affiliations`/`Roles` distinct by `ValueKey`, stored order, `HasAffiliation`/
    `HasRole(key)`) and the options `Affiliations`/`Roles` (`AttributeOption(Key, Label)`, distinct by `ValueKey`, label
    from the first page in ordinal name order, sorted by label ignoring case, then ordinal); `Shown(value)` turns exactly
    one `[[X]]` into the trimmed `X`. `DesktopState.PeopleAttributes()` runs it under `indexGate`, never throws. The panel
    renders by name from the snapshot until it loads; then `select.people-affiliation`/`select.people-role`
    (`aria-label`, first option `All` = `""`, values = keys, text through `OneLine`; a filter with no values is not
    rendered) above the list, `button.people-clear` `Clear filters` while one is set, grey `No people match these
    filters.` in place of the list, and per row a grey `span.person-details` in the button (roles `, ` joined, ` · `,
    affiliations, through `OneLine`). A bad index: everyone, no filters, red `Can't read the attributes.`. Reloads on
    `State.Changed` keeping the rows; a selection that is gone resets to `All`; older results are dropped.
    **Tags and links:** `TagsPanel` (the `Tags` button in `NotesPanel`, or `/tags` without an argument; `Main`'s
    `page`) lists `DesktopState.Tags()` (`TagsView.Build`): one button per tag with its grey note count; job 295: when `TagsView.Skipped` (filled for `AllTags` only) is not empty, a grey `p.tags-skipped` `Not indexed (over 5 MB): <a>, <b>.` follows (names through `DesktopText.OneLine`, at most `MaxSkippedNames` (10), then `, … <k> more`), in the empty-tags state too; the command line `tags` output is unchanged; a click
    shows `TagPanel` for it below the list (`aria-pressed` on that button); its × hides both, `TagPanel`'s only
    itself, and opening a note or `Today` hides both. `/tags <tag>` opens `TagPanel` directly. `LinksPanel`
    sits under `NoteEditor` and lists `DesktopState.Links(name)` (`LinksView.Build`): per `NoteLink` the name (a button
    through `RequestOpen`; a missing target is grey text), the link text (see Note links) and the shared tags as buttons
    (`TagPanel`); grey `<name> has no linked notes.`, red `Can't read the links.`, and for a sensitive note only grey
    `Sensitive notes have no links.` with no lookup. Both read only (no `Actions.`), run under `indexGate`, never
    throw, and reload in `StateComponent.OnStateChangedAsync` (called after every `State.Changed`) keeping the shown
    rows meanwhile; a result for another note or an older request is dropped.
    `PersonPanel` (job 156) sits above `LinksPanel` on a person page (`People.PersonOf(name)`) and lists
    `DesktopState.Person(name)` (`PersonView.Build`): `Mentioned in` note buttons (through `RequestOpen`) and
    `Appointments` (date, time text, one-line title and location), then (job 159) `Open todos` and `Done todos`; grey
    `No notes, appointments or todos mention <name>.`, red `Can't read the mentions.`, red `Can't read the todos.`; above all rows (once loaded, in every state) one red `p.alias-clash` (`role="status"`) line per
    `PersonView.AliasClashes` entry; it follows the same read-only, `indexGate`, reload and stale-result rules. A
    mention chip calls `OnPerson`; `Main` (asking first when dirty) calls `DesktopActions.OpenPerson` and opens the
    result, or shows its red refusal.
    `FindPanel` (job 157, `/find <attribute> <value>`: split at the first space, `NormalizeKey`, value collapsed;
    a bad one shows red `Type /find <attribute> <value>.`) sits after `TagPanel` with the heading `<key>:: <value>`
    and a Close button, and lists `DesktopState.Find(key, value)` as note buttons (through `OnOpen`); grey
    `Loading…`, grey `No notes have <key>:: <value>.`, red `Can't read the attributes.` (red usage line for an `InvalidQuery`); same read-only,
    `indexGate`, reload and stale-result rules. Opening a note closes it.
    `SearchPanel` (job 170, `/search <text>`, the top bar's Search popover (job 218) and saved searches through
    `Main.ShowSearch(text)`, which bumps `searchRequest` so the same text starts over; job 285: `Main`'s `Page.Search` with `searchShown`, a page in place of the note area, so the
    outline is not rendered; another page, Esc or the icon × closes it) has the heading `Search: <text>` (`Search` when blank) and the icon-only × (`Close`), then `input[type=text].search-input`
    (job 207; `aria-label="Search"`, placeholder `Search notes`, maxlength 200, focused on every request; Return searches
    the typed text, Esc closes). A tag filter (`TagFilter.IsTagFilter`) lists `DesktopState.TagFilter(text)` (under
    `indexGate`, never throws) as `note-button`s with no line: grey `No notes match <filter>.`, red `Type a tag filter
    like #project and not #done.` for `Invalid`, red `Search text can be at most 200 characters.` for `TooLong`. Other
    text lists `DesktopState.Search(text)` (`SearchView.Build` under `indexGate`, never
    throws): per hit (job 285) an `li` with `div.hit-head` (the `note-button`, text `DesktopText.NoteName`, full name in `title`;
    grey `span.type`, `Types` joined by `, ` through `OneLine`, only with types; grey `span.date`, `Modified` as
    `yyyy-MM-dd` in `State.Zone`) and the grey `span.line` (cut by CSS, full text in `title`): `SearchPanel.SnippetOf`
    splits the raw line at `MatchStart` into before, `<mark>` (the matched characters) and after, each through
    `OneLine`; more than `MaxBefore` (40) characters before it become `…` and the last 40, the cut moved past a
    surrogate pair; tag-filter rows stay name-only; at most `MaxRows` (100), then grey `… <n> more` (both kinds). Job 295: with hits (both kinds) a grey `p.search-count` with `role="status"` comes before `ul.search-hits` (`aria-label="Search results"`): `1 note found` / `<n> notes found`, `n` the full count; none for blank, errors or no matches. Grey `Loading…`, grey `No notes contain
    <text>.`, red `Can't read the search index.`, grey `Type text or a tag filter like #project and not #done.` for blank (nothing read), red `Type at least
    3 characters to search.` and `Search text can be at most 200 characters.`. When results show (`Found`/`NoMatches`
    of either kind) `button.search-save` `Save search` opens `input.search-save-name` (`aria-label`/placeholder `Search
    name`, maxlength 50, focused): Return calls `Actions.SaveSearch(name, trimmed query)` (its only action), Esc hides
    it; the result is `p.search-save-result` (`role=status`, accent on Done, else red). Reload and stale-result rules as
    the other panels. Opening a note or `Today` closes it.
    Keyboard (job 285): the result buttons are a roving tab stop (`tabindex` 0 on one, `-1` on the rest; ArrowDown in
    the input focuses the first, ArrowDown/ArrowUp move and stop at the last, ArrowUp on the first goes back to the
    input; Enter opens, and the click the browser sends after it is ignored until keyup). `OnOpen` carries `(Note,
    Text)`, the shown query; `Main.OpenFromSearch` opens the note through `RequestOpen` (save or discard flow) and
    then shows `button.search-back` (`Back to search`) above the breadcrumb; it shows the page again for that text
    with `Focus` = the note (that row, else the first, is the tab stop and gets the focus). It goes when another note,
    Today, a day or any page opens, the Search page included.
    `DecisionsPanel` (job 180, `/decisions [tag]`; `Main`'s `decisionsShown`, after `SearchPanel`) has the heading
    `Decisions` (with a tag `Decisions: #tag`) and `Close`, and lists `DesktopState.Decisions(tag)` (`DecisionsView.Build`
    under `indexGate`, never throws): per decision a grey `span.date`, a `note-button` (through `OnOpen`, full name in
    `title`), grey `span.people` (only with people) and the one-line `span.text` (cut by CSS, full text in `title`); at
    most `MaxRows` (100), then grey `… <n> more`. Grey `Loading…`, grey `No decisions yet.` / `No decisions in notes
    tagged #<tag>.`, red `Can't read the decisions.`, red `Type /decisions [tag].` for an invalid tag (nothing read);
    same read-only (no `Actions.`), reload and stale-result rules. Opening a note or `Today` closes it.
    `SimilarPanel` (job 268, `/similar`; `Main`'s `similarShown`, after `DecisionsPanel`) has the heading `Similar notes`
    and `Close`, and lists `DesktopState.Similar()` (under `indexGate`, never throws): per pair two `note-button`s
    (`OnOpen`), the grey `span.reason` (`same title`, `similar title`, `<n>% same text` rounded down, joined by `, `) and
    `button.keep-first`/`keep-second` `Keep <name>`; at most `MaxRows` (100), then grey `… <n> more`; grey `Loading…`,
    grey `No similar notes.`, red `Can't read the notes index.`. A Keep button asks `Main.MergeHeld` (either note open
    and dirty: red `Save <name> first.`), then `DesktopState.MergeLinkCount` (`LinkRenamePlan.ForMerge`, read-only) and
    the `ConfirmDialog` `Merge <source> into <target>? Its text is added to <target>, links in <n> notes are updated and
    <source> stays in its history.` (`Merge`); confirm calls `DesktopActions.MergeNotes(target, source, confirmation)`:
    refusals (no confirmation, `Missing`, same note, sensitive `Locked` with no key call, subpages, attachments in another
    folder, over `MaxEditBytes`, a failed link round trip) write nothing; else the source's bytes are kept through
    `HistoryStore.Keep`, the target is saved as its text without trailing line breaks + `\n\n` + the source's text (BOM
    dropped, its own links rewritten) through `SaveIfUnchanged(..., HistoryStore)`, the source removed with `Delete(...,
    removeHistory: false, removeAttachments: false)` and other notes' links rewritten as on rename (`LinksLeft` text
    `Merged <s> into <t>, but links in <k> notes couldn't be updated: ...`). The result is `p.similar-result` (accent on
    Done); `Main.Merged` opens the target when the source was open and reloads an open clean target. A merged note is
    recovered by creating a note with its name and opening History.
    **Import and export:** the only paths the desktop reads or writes outside the data folder come from the OS
    dialog (`IFileDialog.OpenMarkdownFiles()`: several `*.md`, empty when cancelled or failed;
    `SaveExportFile(suggestedName)`: `null` when cancelled, adds `.md` to a name without extension; Photino's save
    dialog takes no file name, so `PhotinoFileDialog` shows the suggestion in the dialog title), never from typed
    text, and both actions still re-check everything. `DesktopActions.ImportNote(path, Confirmation?)` (not fully
    qualified: `Can't read <path>.`; `NoteStore.Contains`: `That file is already in the Context folder. Nothing
    changed.`; `<ValidateTitle text> Rename the file and import it again.`; `<name> is sensitive. Nothing was
    imported.`; `NoteStore.ReadImport`: `No such file: <path>`, `Can't import a file over 5 MB: <path>`, `Can't read
    <path>.`; an existing plain note without a confirmation is Refused `NotConfirmed` with the note's name as text;
    else `NoteStore.Save` and `Imported|Replaced <name>. The source is unchanged; delete it if you don't need the
    plain copy: <path>`) and `ExportNote(name, path)` (`Export to a path outside the Context folder. Nothing was
    written.`; `<name> no longer exists. Nothing was done.`; `<name> is sensitive. Make it plain to export it.`
    without any `Agree`; an existing file or directory: `That file already exists; choose a new name: <path>`;
    `Can't read <name>.`; `NoteStore.WritePlainCopy` with `FileMode.CreateNew`: `Exported <name> to <path>` or `Can't
    write <path>.`; no refresh). Only the replace question carries `NotConfirmed`, no answer holds an exception message, and
    every path and name shown goes through `DesktopText.OneLine`. `NotesPanel` has `Import` (or `/import`, which
    `Main` passes to `NotesPanel.Import()`; a typed argument is ignored): at most 100 files (`Choose at most 100
    files.`), one line per file in the chosen order in a `role="status"` region that is always in the page (accent Done, red otherwise, cut by
    CSS with the full text in `title`); files that would replace a note are written only after one `ConfirmDialog`
    (`Replace <name>?` or `Replace <n> notes? <names>`, confirm label `Replace`), and Cancel or Esc leaves grey `Not
    replaced: <name>`. The More actions menu of a plain note has `Export` (or `/export`; argument ignored; no note open: red
    `Open a note first.`; a sensitive note has no item and `/export` shows the sensitive text without a dialog):
    a dirty editor is refused with `Save <name> first.`, else `SaveExportFile("<name>.md")` and `ExportNote`'s text
    in the main status line.
    **Share (job 250):** `NoteHtml.Render(title, text, resolveImage, leaveOutPrivate)` (Core, pure, reads no file)
    makes one self-contained page: `<!DOCTYPE html>`, `<meta charset="utf-8">`, the CSP meta `default-src 'none';
    img-src data:; style-src 'unsafe-inline'`, the encoded `<title>`, one inline `<style>`, no script. It renders
    headings h1-h6, paragraphs, `-`/`*`/`1.` lists nested by indentation (tasks as ☐/☑), `>` quotes, fenced code as
    `<pre><code>`, code spans, `**strong**`, `*em*`/`_em_`; everything else (tables too) is a paragraph, and all text is
    encoded. Tags show as `TagParser.Format`, `[[Note]]`/`[[Note#Heading]]` as the target and `@[Name]` as the name,
    never as links (inside code as written); `[text](url)` is an `<a href>` only for http/https/mailto (any case),
    else just the text; `![alt](src)` is a `data:` image only among `NoteAttachments.ImageSources` (the 20-image cap)
    and when `resolveImage` gives an image, else `[image: alt]`. With `leaveOutPrivate` every line on which
    `TagParser.LineHasTag(line, "private")` (outside fences) is left out. `DesktopActions.ShareNote(name, path,
    leaveOutPrivate)` refuses like `ExportNote` (path, inside, missing, exists, `Can't read <name>.`, `Can't write
    <path>.`; same texts), writes with `WritePlainCopy` and says `Shared <name> as HTML: <path>`. A sensitive note is
    never shared: locked or open it is Refused/`Locked` with `<name> is sensitive. Make it plain to export it.`
    (`ExportSensitiveText`) without any `Agree`, and nothing is written. `IFileDialog.SaveShareFile(suggestedName)`
    adds `.html`. The More actions menu of a plain note has `Share` (a sensitive note has no item; `/share` shows the
    sensitive text in red without a dialog): no note open: red `Open a note first.`; a dirty editor: `Save <name>
    first.`; else `ShareDialog` (wraps `ConfirmDialog` through its `ChildContent`; the `Confirmation` it makes is not
    passed on, `OnChoose` carries only whether to leave out `#private` lines): the checkbox `Leave out lines tagged
    #private` (checked), `Choose file…` / `Cancel`, Esc cancels. Choose file calls `SaveShareFile("<leaf>.html")`; a cancelled
    save dialog shows nothing, else `ShareNote`'s text in the main status line (accent Done, red otherwise).
    Rules (`DesktopSourceTests`): `WritePlainCopy` and `ReadImport` are named in
    `Context.Desktop` only in `DesktopActions.cs`, and no `.razor` file matches `\b(File|Directory|Path)\.`.
    **Sidebar bar (job 197):** `NotesPanel`'s bar holds only the icon buttons (`.notes-bar-buttons`: triage, people, tags, settings; Search moved to the top bar in job 218, `NotesPanel.OnSearch` stays for saved searches), right-aligned (`justify-content: flex-end`); no logo and no visible "Notes" heading, the list `ul.note-list.recent-notes` carries `aria-label="Recent notes"` (job 240). Between the bar and the list, `nav.saved-searches` (job 207, `aria-label="Saved searches"`, only with entries; `DesktopSnapshot.SavedSearches`) has per entry `button.saved-search` (name through `OneLine`, cut, `title` the query; `OnSearch(query)`) and `button.saved-search-remove` (`×`, `aria-label`/`title` `Remove <name>`; `Actions.RemoveSavedSearch`, result in grey/red `p.saved-search-result`); `SavedSearchesUnreadable` shows red `Can't read saved searches.` instead. **Placeholders:** a box's placeholder says only what it is for (`New todo`, `Capture to inbox`), never a `Return to …` hint; Return still submits and there are no submit buttons.
    **Tidy layout (job 192):** the Triage, People (job 239), Tags and Settings buttons in `NotesPanel`'s bar are icon-only (inline `svg`, `aria-hidden`, `currentColor`; `aria-label` and `title` carry the name), note rows show no modified date, and `.top-bar` is a band (`--panel` background, `--line` bottom border).
    **Top bar (job 198):** `TopBar` replaces the capture box as the first child of `Main`'s `main.current-region` (not with
    `Can't read the data folder.`): an empty `.top-bar-start` and right-aligned `.top-bar-buttons` with icon buttons in the order Today, Search, Capture, Pick a day, More actions (job 241)
    `button.today-open` (`Today`), `button.search-open` (`Search`, job 218: the magnifying glass; a `.search-pop` popover
    with a `.search-pop-backdrop`, styled like `.capture-pop`, with one `input[type=text]` `aria-label="Search"`,
    placeholder `Search notes`, maxlength 200, empty and focused on each opening; Return calls `OnSearch(text)` once
    (`Main.ShowSearch`, empty text too) and closes, so the panel's input takes the focus; Esc closes and focuses
    `button.search-open`), `button.capture-open` (`Capture`, job 199: a hand-drawn "T" with an I-beam caret; a `.capture-pop` popover with one
    `input[type=text]` `aria-label="Capture"`, placeholder `Capture to inbox`, maxlength 1000, focused on open, and a
    `.capture-pop-backdrop`), and `button.day-pick-open` (`Pick a day`; job 213: a `.day-pick` month calendar with a
    `.day-pick-backdrop` and no input: header `button.day-pick-prev`/`day-pick-next` (icon-only, `Previous month`/`Next
    month`, disabled on January 1900/December 2100) around `.day-pick-month` (`MMMM yyyy`, invariant, also the
    `table[role=grid]`'s `aria-label`); columns an empty week header then `Mo`..`Su`; always 6 rows from the Monday on or
    before the 1st, each a `th.week[role=rowheader]` (`ISOWeek` of its Monday, `aria-label` `Week <n>`) and 7
    `td[role=gridcell]` > `button.day` (`aria-label` `dddd, d MMMM yyyy`; `outside` for other months, still clickable;
    disabled outside 1900-01-01..2100-12-31); `TopBar` parameters `Today` (`today`, `aria-current="date"`) and
    `Selected` (`selected`, gridcell `aria-selected="true"`; `Main.SelectedDay`: the open note's day when its name is
    exactly `yyyy-MM-dd`, else null, job 216). Each opening
    starts on `Selected ?? Today` with that day as the one `tabindex="0"` day (roving tabindex) and focuses it; arrows
    move ±1/±7 days, PageUp/PageDown ±1 month (day clamped), never outside the range, the shown month follows; a click or
    Enter calls `OnPick` once and closes (a later event finds it closed). Date maths in `MonthGrid.cs`; styles only
    theme variables, `.day.today.selected` keeps an inner ring). Job 289 moved the popover into `DayPicker.razor`
    (parameters `Today`, `Selected`, `OnPick`, `OnClose`, optional `OnToday`; rendered only while open, so each opening
    is a new instance; Esc inside `.day-pick` or the backdrop calls `OnClose`), shared with `DayAppointmentsPanel`; the
    top bar passes no `OnToday`, so it has no `button.day-pick-today`. Esc, the backdrop or the button
    closes a popover; opening one closes the others, so at most one is open. `Main.ShowToday` (the `Today` button and `/today`, job 216) opens today's daily note in the page editor as
    `/today` describes (created when missing; another dirty note saved first, else the discard dialog; today's note
    itself stays as it is and a shown page closes). At start `Program.cs` calls `DesktopActions.AutoOpenToday()`, which
    ensures today's note like `OpenDailyNote()` and offers it through `DesktopState.OfferStartNote(name)` (a failure
    offers nothing); `Main` takes it (`TakeStartNote`) in `OnInitialized` and after every `State.Changed` once a snapshot
    exists, and opens it only while no note is open and no page, dialog or edit field is shown, else drops it.
    `PickDay(DateOnly)` (job 212) works like `/today` for that day: it opens the day's daily note (`yyyy-MM-dd`) in the
    page editor through `Actions.OpenDailyNote(day)` (`DailyNote.Ensure` with `DailyNote.Compose` for that day; the
    no-argument `OpenDailyNote()`/`DailyNoteName()` use the local clock's day), the `running` gate and `ShowDaily`
    (accent `Created <name>`, red failure text, the unlock flow for a sensitive note). The open note of that day stays
    as it is; another dirty note is saved first (`KeptUnsaved`), and when that fails the day waits in
    `pendingPickedDay` behind the `Discard changes to <name>?` dialog. There is no read-only day view and no dashboard: `CurrentPanel` was
    removed in job 216.
    **More actions menu (job 200):** the last top-bar button `button.page-menu-open` (three dots, `aria-label`/`title`
    `More actions`, `aria-haspopup="menu"`, `aria-expanded`) opens `div.page-menu[role=menu]` with a
    `.page-menu-backdrop`; it is one of the three mutually exclusive popovers. `TopBar` gets `[Parameter]
    IReadOnlyList<PageAction> PageActions` (empty: no button), `PageActionsDisabled` (`running` or an open
    Rename/New subpage/Move form) and `EventCallback<string> OnPageAction`. `PageAction(id, label, checked)` is a
    public record (a parameter type must be public); its id is the item's class, `checked` non-null makes a
    `menuitemcheckbox` with `aria-checked`. `Main.PageActionsFor(snapshot)` builds the list: none while a page
    (Triage, People, Tags, Settings, Appointment) is shown and with no note open; `end-day` then `standup` first on today's daily note (`DailyNote.NameFor(Snapshot.Today)`); for a plain note `rename`,
    `delete`, `history`, `new-subpage`, `move`, `open-external`, `export`, `share`, `mark-sensitive` (when supported); for a
    sensitive note `rename`, `delete`, `history`, `make-plain`, `lock`. `Main.RunPageAction(id)` (the bound handler)
    runs the chosen one. Keys in the menu (default prevented): opening focuses the first item, ArrowDown/ArrowUp
    move and wrap, Return or Space runs the focused item, Esc or Tab closes the menu and focuses the … button.
    **Quick capture:** the capture box is gone (job 198); capture is the palette's `/capture <text>` or the top bar's
    Capture popover (job 199). Both run `Main.CaptureAsync(string text)` (`Task<bool>`, true when Done; the only
    `Actions.CaptureText(` call in `Main.razor`): `CaptureHeld`, the `running` gate, `CaptureText`, the main status line
    and `Captured()`. `TopBar` gets it as `[Parameter] Func<string, Task<bool>> OnCapture` (a plain `Func`, so
    `CaptureAsync` calls `StateHasChanged` itself); Return in the popover awaits it and closes the popover and clears the
    field only on true, otherwise text and popover stay. The source rule "Actions only from event handlers" lets a method
    that calls `Actions` be called by another method only when that caller is itself a bound handler. It calls
    `DesktopActions.CaptureText(text)` once and answers in the main status line:
    `CaptureText` cleans the text with `Inbox.Clean` (empty: Refused `Type something to capture.`; over 1000: `A
    capture can be at most 1000 characters.`), refuses a sensitive inbox without any key-store call (`inbox is
    sensitive. Nothing was captured.`) and then calls `Inbox.Capture` with the injected local clock (`Func<DateTime>`
    constructor argument; `Program.cs` passes `() => DateTime.Now`, `DesktopFixture` `LocalNow`): Done `Captured.`
    and a refresh, `inbox is too large. Nothing was captured.`, `inbox kept changing on disk. Nothing was captured.`,
    or Failed `Can't write inbox. Nothing was captured.` (test hook `BeforeCaptureSave`, run before every save
    attempt). The palette's `/capture <text>` runs the same action and answers in the main status line (`/capture`
    alone: red `Type /capture <text>.`). It asks `Main.CaptureHeld` first:
    with the plain inbox note open and unsaved changes nothing is written and the line is red `Save <name> first.`
    After a capture `Main.Captured` calls `NoteEditor.ReloadAsync` when the inbox is open
    without unsaved changes: the note is read again and the page editor mounted with it, without taking the focus,
    so the new line shows and a later save is not refused as changed on disk; another open note is left alone.
    **Inbox triage (job 179):** `TriagePanel` (`/triage`, argument ignored, or the `Triage` icon button in `NotesPanel`'s
    bar; `Main`'s `page`, a page under the top bar, hidden by the ×, Esc or opening a note or the current
    view) shows the first line of `DesktopState.Triage()` (`InboxTriage.Items`: every `- ` line; text without the
    `- `, a leading `yyyy-MM-dd HH:mm ` stamp, shown grey, and a final ` snooze:yyyy-MM-dd`; lines snoozed past today
    are left out and counted, due snoozed lines come first by day, then file order), `<n> left` and the buttons
    `Note (N)`, `Todo (T)`, `Delegate (W)` (job 243), `Add to note (A)`, `Drop (D)`, `Later (L)`; the keys n/t/w/a/d/l (any case, outside a text
    field, whose keys stop at the field) do the same, 1/2 pick Tomorrow/Next week in the Later form, Esc closes the
    form or the panel, Enter in a field submits, and after every Done the next line shows with the focus on the panel.
    Empty: grey `The inbox is empty.` plus ` <n> lines are snoozed.` (` 1 line is snoozed.`); a sensitive inbox: red
    `inbox is sensitive. Nothing to triage.` with no key-store call. The actions are `DesktopActions.TriageToNote`
    (title pre-filled, cut to 100; `# <Title>\n\n<text>\n` via `CreateWithText`), `TriageToTodo` (empty date: today),
    `TriageAppend` (name matched like `/open`; `- <text>\n` after a missing final newline, via `ReadForEdit` +
    `SaveIfUnchanged`; the inbox is refused with `Pick a note other than inbox.`; a sensitive target is read by the
    panel through `DesktopState.ReadSensitive`, re-sealed through the `SaveNote` path and closed in the session
    again afterwards unless it was held before), `TriageDrop(item, Confirmation?)` (ConfirmDialog `Drop this line?
    <text>`) and `TriageSnooze` (a day after today; the line is rewritten in place). Every target write first checks
    that the inbox still holds the exact line (else Refused `That line is no longer in the inbox. Nothing was
    done.`); the line is removed (`InboxTriage.Remove`, first equal line, other bytes, CRLF and BOM kept) only after
    the target was written, and when that fails the result is Failed/`LineKept` `Saved to <target>, but the line is
    still in the inbox.` (test hook `BeforeTriageRemove`). `Main.TriageHeld` (the panel's `Guard`) refuses with `Save
    <name> first.` while the open inbox or the open target has unsaved changes; `Main.Triaged` reloads an open clean
    inbox or target (a sensitive one through `ShowSavedAsync`).
    **Waiting for (job 243, Core `WaitingFor.cs`):** a todo whose text starts with `waiting: @[Full Name] <text>`
    (`WaitingFor.TryParse`: optional leading whitespace, `waiting:` ignoring ASCII case, optional spaces/tabs, then a
    valid `@[Name]` mention right there; `Person` normalized, `Rest` trimmed) is a waiting-for todo; its due date is the
    follow-up date and nothing else is stored (`todos.json`, `context.db` unchanged). `TodoPanel` leaves open ones out
    of `ul.todo-list` and shows them, same rows and order, in `section.waiting-for` (`h3` `Waiting for`,
    `ul.waiting-list`) below it, only with rows. `PersonView.Waiting` holds the open stored ones waiting on the person
    (`aliases.PersonKey`), left out of `OpenTodos`; `PersonPanel` shows them as `Waiting for` before `Open todos`.
    **Topics for the next 1:1 (job 253, Core `TopicParser.cs`, `MeetingTopics.cs`):** a line outside fences that is
    optional spaces/tabs, optionally `-`/`*`/`+` and one space, optionally `[ ]` and one space, then `next:` (ASCII,
    ignoring case), optional spaces/tabs and an `@[Name]` mention right there (the `WaitingFor` rules) is a topic
    (`ParsedTopic(Line, Person, Text, Raw)`; text after the mention collapsed, trimmed, cut to 1000; empty or `[x]`:
    none). `TagIndex.Topics()` gives `NoteTopic(Note, Line, Person, Text, Raw)` rows; `MeetingTopics.For(topics,
    aliases, names)` keeps those whose person resolves (`PersonKey`) to one of `names`, ordered by that name's first
    position, then note ignoring case, then ordinal, then line. `MeetingTopics.Complete(store, topic, history?)`
    works like `NoteTodos.Complete` (same `NoteTodoResult` and texts) and rewrites the line to `TopicParser.DoneForm`:
    `[ ]` → `[x]`, `- next:` → `- [x] next:`, bare `next:` → `- [x] next:` (indent kept). `PersonView.Topics` (never
    counted for `Kind`) shows in `PersonPanel` as `Topics` (`ul.person-todos.topics-list`, `li.topic`) before `Waiting
    for` (alone when there are no mentions); `AppointmentPageView.Topics` (`DesktopState.AppointmentPage`, the
    attendees, under the index lock) shows in `AppointmentPanel` as `section.topics` (`h3 Topics`, rows `Person: text`)
    above `h3 Notes`, only with rows and a readable index. Each row: `input.done` asks through `ConfirmDialog`
    (`Mark this topic done?`), then `DesktopActions.CompleteTopic(topic, confirmation)`; a non-Done result is the red
    `p.topic-result`; `button.topic-note` opens the note. `OpenMeetingNote` passes `DesktopState.TopicsFor(attendees)`
    to `MeetingNote.Ensure` when the note doesn't exist yet. The CLI is unchanged.
    **Review dates (job 256, Core `ReviewDateParser.cs`, `NoteReviews.cs`):** the first line outside fences that is
    optional spaces/tabs, optionally `-`/`*`/`+` and one space, `review:` (ASCII case ignored), optional spaces/tabs,
    `yyyy-MM-dd` (ASCII digits, a real date) and only trailing spaces/tabs is the note's review date
    (`ParsedReview(Line, Due, Raw)`; `SetDate(raw, day)` swaps only the date). `TagIndex.Reviews()` gives
    `NoteReview(Note, Line, Due, Raw)` rows of notes with a stored time; `NoteReviews.Due(reviews, today)` keeps
    `Due <= today`, ordered by due, note ignoring case, then ordinal. `NoteReviews.Reschedule(store, review, day,
    history?)` and `Remove(store, review, history?)` (the line and its line end; a last line without one takes the line
    end before it) work like `NoteTodos.Complete` (same `NoteTodoResult` and texts). `DesktopSnapshot.Reviews` (the due
    rows) and `ReviewsUnreadable` are filled in `DesktopState.Build` under the index lock after `NoteTodos.Load`;
    `DesktopActions.RescheduleReview(review, months)` (today from the local clock + `AddMonths`, 1-120) and
    `RemoveReview(review, confirmation)`. `ToReviewPanel` (after `TodoPanel` in `aside.todos-region`, only with due
    rows or unreadable: `section.to-review`, `h3 To review`, red `Can't read the review dates.`) has per
    `li.review-item` `button.review-note` (opens), grey `since yyyy-MM-dd` when overdue, `button.review-3m`,
    `button.review-1y` and `button.review-remove` (`ConfirmDialog` `Remove the review date from <name>?`); a
    non-Done result is the red `p.review-result`. `Main.ReviewHeld` refuses with `Save <name> first.` when the note is
    open with unsaved changes (nothing saved); `Main.Reviewed` reloads the open, clean note. `Briefing.Compose` writes
    `## To review` after `## Follow-ups`: `- [[<name>]]` per due note (` (due yyyy-MM-dd)` when overdue), else
    `- Nothing to review.` or, with `null` reviews (`DesktopState.ReviewsForBriefing`), `- Can't read the review
    dates.`. The CLI is unchanged.
    `Briefing.Compose` writes `## Follow-ups` after `## Todos`: `- <Person>: <Rest>` per open stored one with
    `Due <= today` (` (due yyyy-MM-dd)` when overdue), else `- No follow-ups due.` or `- Can't read todos.json.`.
    Triage's `Delegate (W)` (after Todo; key w) form has Person (focused), Todo text (prefilled) and Follow-up date
    (empty: today+7); `DesktopActions.TriageDelegate` adds `WaitingFor.Compose(person, text)` through `AddTodo` and
    removes the line like `TriageToTodo`; a name `NormalizeTyped` refuses or that doesn't read back (a `]`) is red
    `Type a person's name.` (`TriagePersonText`).
    `TodoPanel` has an entry row (`DesktopActions.AddTodo`). `NotesPanel` has a new-note entry row (text field + `New
    note`; Enter or the button calls `CreateNote`; on Done the field clears, the note opens through `Main`'s `RequestOpen`
    (a dirty editor asks first) and a `role="status"` line shows `Created <name>` (accent), else the red text). While a
    note is open its actions are in the top bar's More actions menu (also when the note can't be read; job 200), and
    `.note-toolbar` above it exists only while a form is open: `Rename` swaps in a prefilled name
    field (Enter or `Save name` renames and reopens the note under the new name; Esc or `Cancel` closes the form;
    a dirty editor is refused with red `Save <name> first.`), `Delete` asks `Delete <name>? This can't be undone.`
    (`ConfirmDialog`, confirm label `Delete`; on confirm the main region shows the `p.no-note` placeholder) and `Open in
    default editor` calls `OpenExternally` (red text on failure). Recently opened notes (job 240, replacing job 152's tree):
    `NotesPanel` draws only the notes opened most recently, one flat `ul.note-list.recent-notes` (no toggles, no nested
    lists): the entries of `State.Recent` that name an existing note (ordinal match against `Snapshot.Notes` or
    `Snapshot.Sensitive`), newest first, at most `RecentNotes.MaxCount` (10). Each `li.note` holds a `button.name.cut`
    with `DesktopText.NoteName(Title)`, for a subpage followed by a grey `span.note-parent` with the parent's full name
    (through `OneLine`); its `title` is the full name, the row of `OpenName` has `aria-current="page"`, and a sensitive
    note keeps the visible `locked`. With notes but no listed entry it shows grey `No recently opened notes.`
    (`p.recent-empty`); `No notes yet` and the red unreadable line stay. Core `RecentNotes.cs` holds the pure steps
    (`Touch`, `Renamed`, `Removed`, `IsValidName`) and `RecentNotesStore`: `<root>/recent.json`,
    `{"recent":[…]}` UTF-8 without BOM, relaxed escaping, temp file then move, never creating the root; read through
    `BoundedFile` (64 KiB), anything broken, of another shape or with an invalid name reads as empty and the next write
    replaces it. `DataWatcher` ignores the file; the CLI never names it. `DesktopState` loads the list once
    (`Recent`, `ChangeRecent`). `Main.ShowNote` calls `Actions.RecordOpened(name)` whenever the open name becomes a
    different non-null name (every route, also today's note at start), except the reopen after a rename or move of the
    open note (`record: false`), since the action already applied `Renamed`. `RecordOpened` runs outside `Run` (never
    Busy), never refreshes or throws, raises `Changed` and writes the file on `Task.Run` under its own lock (a failed
    write is swallowed, the in-memory list stays); it is the one `Actions.` call `DesktopSourceTests` lets a non-handler
    method make. `RenameNote`/`MoveNote` apply `Renamed` and `DeleteNote` applies `Removed` when Done. In tests,
    `NoteOpener.OpenAsync` opens a note through `/open`, and `DesktopFixture.WriteRecent` seeds the file before the
    state is built. A test that snapshots the data folder after an open first calls `DesktopFixture.WaitForRecentWrites()`,
    a bounded poll until the internal `DesktopActions.PendingRecentWrites` (writes `RecordOpened` started, counted with
    `Interlocked`, uncounted in the task's `finally`) is 0; `DesktopFixture.HoldRecentWrites()` holds the write gate on a
    thread of its own (job 246). `FileSnapshot.Take` also leaves out the temp files of every ignored file
    (`.<name>.<anything>.tmp` in that file's own folder, checked on the path before any read), so a snapshot that
    ignores `recent.json` never sees or reads a write in flight; a test that proves no temp file is left lists the
    folder itself (job 267). Pinned notes (job 278): the note menu's `pin` item (`menuitemcheckbox` `Pinned`, after
    `History`, `aria-checked` from `State.Pinned`; only with a note open, plain or sensitive) calls
    `Actions.SetPinned(name, pinned)` (`Run`, `refresh: false`): an already matching state is Done/Unchanged, a 21st pin
    Refused with red `You can pin at most 20 notes.`, a failed write Failed with red `Can't write pinned.json.` (the file
    is written first, `DesktopState.ChangePinned` only after, so the in-memory list stays). `NotesPanel` draws
    `ul.note-list.pinned-notes` (`aria-label="Pinned notes"`, pinned order, existing notes only) above the recent list
    with the same rows plus an inline svg `span.pin-icon` (`role="img"`, `aria-label`/`<title>` `Pinned`,
    `currentColor`) after the name; the recent list leaves pinned notes out (all pinned: grey `No recently opened
    notes.`). Core `PinnedNotes.cs` holds the pure steps (`Pin`, `Unpin`, `Renamed`, `Removed`, at most 20) and
    `PinnedNotesStore` (`<root>/pinned.json`, `{"pinned":[…]}`), sharing `NameListFile` (in `RecentNotes.cs`) with
    `RecentNotesStore`, so it has the same read and write rules. Rename/move apply `Renamed`, delete/merge `Removed`,
    writing the file only when the list changes (so tests that never pin never see it). `DataWatcher` ignores it; the
    CLI never names it. `DesktopFixture.WritePinned` seeds the file before the state is built. A whole-folder snapshot that includes `context.db` while panels may still load goes
    through `DesktopFixture.SnapshotUnderIndexGate(syncFirst)` (under `IndexGate`; `syncFirst: true` for "before"), since
    reading the file during a panel's index sync fails on macOS. An open subpage shows `<nav class="breadcrumb" aria-label="Breadcrumb">`
    as the first child of `.note-area`: each ancestor a `button.crumb` opening it through `RequestOpen`, the last item a span with
    `aria-current="page"`.
    Below `NoteEditor`, before `HistoryPanel`, `PersonPanel` and `LinksPanel`, a note with subpages shows `<nav
    class="subpages" aria-label="Subpages">` (`h2` `Subpages`; job 238): one `button.subpage-link cut` per direct child
    from `Snapshot.Notes` (no disk read; `Title` ignoring case, then ordinal), text `DesktopText.NoteName(Title)`,
    `title` the full name through `OneLine`, opening it through `RequestOpen`, and a grey `span.subpage-count`
    (`1 subpage`/`<n> subpages`) for a child with children of its own; no children, no `nav`.
    Below it the editor's `h2.note-title` shows only the page's own name (the last segment) and its `title` attribute
    holds the full name (job 235); the unreadable and editor-failed lines and the status lines keep the full name. Both
    go through `DesktopText.NoteName` (`OneLine`, then every bidi control U+202A-202E/U+2066-2069 becomes `�`).
    The plain-note menu also has `New subpage` (field `Subpage title`, `Create subpage` /
    `Cancel`, Enter/Esc; Done opens the page and shows `Created <full name>`) and `Move` (field `New parent`, prefilled
    with the parent, empty = top level, `Move here` / `Cancel`; Done reopens the note and shows `Moved to <name>`);
    both, like Rename, refuse a dirty editor with `Save <name> first.`. Rename prefills `Title`, Export suggests
    `<Title>.md`, and Delete of a note with subpages asks `Delete <name> and its <n> subpages? This can't be undone.`
    (`1 subpage` for one). The palette's `/new <title>` does what the entry row
    does (`/new` alone: red `Type /new <title>.`). The page editor is `wwwroot/editor/editor.js`
    (`globalThis.ContextEditor`), built from `editor/src/` (`schema.js`, `markdown.js` with markdown-it `html: false`,
    `view.js`, `index.js`, `mermaid.js`, `paste.js`); the JS `isLossless` stays for the bundle tests, but the desktop never calls it (job 210: no
    reformat line, no `.lossy`, no `EditorInterop.IsLosslessAsync`).
    Job 263 (Mermaid): `code_block` keeps its opening fence in the `fence` attr, so ```` ``` ````, `~~~` and longer fences
    round-trip for every code block (the serializer keeps the fence character and makes it longer than any run of it in
    the text). A fence whose first info word is `mermaid` (any ASCII case, `isMermaidFence`) gets the node view in
    `mermaid.js`: the code (contentDOM), a grey hint (`Drawing diagram…`, `Can't draw this diagram.`) and the diagram as
    an `<img alt="Diagram">` from a `blob:` URL (revoked on redraw and destroy); the code is hidden while drawn unless the
    selection is in the block (`mermaid-editing` decoration), and a click on the diagram moves the cursor there. Mermaid
    runs only in one lazily made hidden iframe per page, `wwwroot/mermaid/frame.html` (hand-written; its own CSP
    `default-src 'none'; script-src 'self'; style-src 'unsafe-inline'; img-src data:; base-uri 'none'; form-action
    'none'`; one script `mermaid-frame.js`), with `sandbox="allow-scripts"` only: never `allow-same-origin`, never loosen
    its `script-src`. Messages are checked by `event.source` both ways and posted with target `"*"` (the frame's origin
    is opaque); the page takes only `mermaidResult`-checked SVG strings (`<svg`, at most 5,000,000 chars) and sends
    only text that passes `shouldRender` (not blank, at most 50,000 chars), one request per block at a time. Mermaid
    is initialised with `securityLevel: "strict"` and no HTML labels; its error text never leaves the frame. The frame
    element's off-screen rule `.mermaid-frame` is in `app.css` (`editor.css` styles only `.note-editor-body`).
    Share HTML, export and the CLI show Mermaid fences as code.
    Job 202: `NoteEditor` passes `Name` to `mount` (5th argument); `splitTitle(markdown, name)` keeps a
    first line `# <name>` (ignoring case and surrounding spaces, or the name's last segment) and the blank lines after
    it as a hidden head that is never parsed and that `getMarkdown`/`roundTrip` put back byte for byte, and inside the
    editor a level-1 heading keeps `level` 1 (saved as `# `) but is drawn as `h2.heading-level-1` (`headingDom`; job 215: `editor.css` sizes it 2em, `h2` 1.6em, `h3` 1.3em, h4–h6 keep the browser defaults), while typing `# ` makes level 2 (`typedBlock` tests the input rules without a view).
    **Attribute box** (job 236): `attributeSplit(body)` (internal; test hook `attributeSplitJson`) cuts the body after the
    head into `attrs` (the run of attribute-shaped lines from the first body line on: a column-0 key per `KeyEnd`, at most
    50 chars, directly followed by `::`, any value; plus the blank lines right after) and the rest; `mount` keeps `attrs`/
    `cleanAttrs` raw and parses only the rest, `getMarkdown`/`markCleanIf`/`roundTrip` use `joinTitle(head, attrs +
    serialize(doc))`, `isDirty` is also true when `attrs !== cleanAttrs`. `attributeRows(markdown, name)` and
    `attributes(element)` give `[{key,value}]` (key as written, value trimmed); `editAttributes(attrs, editsJson)` (pure;
    `[{line,key,value}]`, `line` -1 for a new row; unchanged rows keep their bytes, changed/new ones `key:: value` with the
    block's line ending, control characters as spaces, malformed edits give `null`) and `setAttributes(element, json)`.
    .NET reads rows with `NoteAttribute.Parse` (all or nothing, at most 1000) and `NoteEditor` shows `details.note-attributes`
    (never `open`) right after `div.editor-bar`, re-read after every mount, `ReloadAsync` and `ShowSavedAsync`; edits
    (value `@onchange`, Remove, Add with `AttributeParser.NormalizeKey`, values `CollapseWhitespace`d) re-read the rows,
    set the dot from `isDirty` and restart the pause.
    A note that was read (or unlocked) but whose page editor didn't start (`mount` threw) shows red
    `The editor couldn't start. <name> was not changed.` (`p.editor-failed`, never together with `.unreadable`) and
    can't be saved, while a failed `focus` after a mount shows nothing.
    **Heading links and the outline** (job 205): `outline(markdown, name)` (no DOM) and `headings(element)` (the live
    document) return JSON `[{level,text}]` of the body's headings in document order (the hidden title left out; text is
    the text nodes plus each chip's raw text, marks dropped, a line break as a space, trimmed); `scrollToHeading(element,
    index)` puts the cursor at the start of the index-th heading, scrolls it into view and returns true (out of range:
    false, nothing changes). `EditorInterop.HeadingsAsync`/`ScrollToHeadingAsync`; .NET reads the list with
    `OutlineHeading.Parse` (all or nothing: a malformed entry, a level outside 1-6 or more than `MaxHeadings` (1000)
    gives none) and sends JS only an integer index. A clicked `[[Plan#Goals]]` resolves through
    `DesktopState.ResolveLink`; another note goes to `NoteEditor.OnOpenHeading` -> `Main.RequestOpen(name, heading)`
    (saves first like any link; the new editor gets `Heading` and jumps once after its mount), while `[[#Goals]]` or a
    heading of the open note jumps in place (no remount, unsaved text kept). The jump picks the first heading whose
    text matches (`OutlineHeading.IndexOf`: trimmed, whitespace runs as one space, case ignored); none: the note stays
    at the top and grey `<name> has no heading <heading>.` (both through `DesktopText.OneLine`). **A link to a missing note**
    (job 233) with a non-empty note part goes to `NoteEditor.OnCreateNote` with the target as written (no brackets);
    `Main.CreateLinked` saves first (`KeptUnsaved`; a failed save asks `Discard changes to <name>?` with the target in
    `pendingLink`), keeps the `running` gate, calls `DesktopActions.CreateLinkedNote` and opens the note through
    `ShowDaily` (accent `Created <name>`, else the red one-line refusal). Without an `OnCreateNote` delegate the editor
    still shows grey `No note named <target>.`. `OutlinePanel` (`nav.outline-panel`, `aria-label="Outline"`) sits in `Main`'s right sidebar
    after `DayAppointmentsPanel` in `aside.todos-region`, before `TodoPanel` (job 210; `.todos-region .outline-panel`: own 40vh
    scroll, `--line` border), and shows one `button type="button"` (`outline-item level-<n>`, plain text) per heading from
    `OutlinePanel.MinHeadings` (3) on; a click calls `scrollToHeading` with its index and never takes focus by itself.
    **Sidebar appointments** (job 214): `DayAppointmentsPanel` (`section.day-appointments`, `aria-label="Appointments"`)
    is the first child of `aside.todos-region` in every state. Its `h2.day-appointments-day` reads `Today` for
    `Snapshot.Today`, else `dddd, d MMM yyyy` (invariant, job 289), `title` always the full `dddd, d MMMM yyyy`;
    icon-only `button.day-prev`/`day-next` (`Previous day`/`Next day`) step the day, disabled at 1900-01-01/2100-12-31 (the chosen day is in memory only; back on today it follows the
    snapshot). Job 289: the `h2` holds one `button.day-appointments-open` (`aria-haspopup="dialog"`, `aria-expanded`)
    that toggles a `DayPicker` (see Top bar) inside the section, on the shown day, with `button.day-pick-today`
    (`Today`); a pick or Today shows that day as the arrows do and closes it; Esc, the backdrop, a pick and Today give
    the focus back to the heading button (`DayAppointmentsPanel.OpenButtonReference` for tests). Off today the bar ends
    in `button.day-today` (`Today`, `title` `Back to today`), which shows today. CSS: the section is `position: relative`,
    its `.day-pick` centred under the bar (`max-width: 100%`, z-index above the top bar's icons). It lists `DesktopState.DayAppointments(day)` (calendar files plus the feed's cached copy through
    `ReadCache` + `CurrentView.FeedEvents`, never a download; `Task.Run`, never throws, with the state's UTC clock in
    `Zone` as `LocalNow`) through `AppointmentList.Shown` (Core, pure: on today the last past appointment, `End <= now`,
    last in `CalendarStore.Day` order, plus every one not yet past; other days all) as `button.appointment-item`
    (`CurrentView.Time` for that day, `OneLine` title or `(no title)`); grey `No appointments.`, red
    `Can't read calendar/<file>`; it reloads on `State.Changed` keeping the rows, a day change shows `Loading…`.
    **Sidebar Times** (job 277): `TimesTodayPanel` (a `StateComponent`) is right after `DayAppointmentsPanel` in
    `aside.todos-region`. `DesktopState` takes the optional `timesFolder` (`Program.cs` resolves it once and passes it to
    the state and the actions); `TimesNow()` (`Task.Run`, never throws) reads `TimesNow.Read` for today and now from the
    state's UTC clock in `Zone`, and every `Tick()` raises `TimesChanged`. The panel renders nothing for `None` and
    before its first load; else `section.times-today` (`aria-label="Times"`) with one grey `p.times-line` (cut, line in
    `title`): `Arrived HH:mm[ · Left HH:mm] · Time left H:mm[ · #<n> <name>| · <name>]` (names through `OneLine`), or
    `Can't read Times' day file.`. It reloads on `State.Changed` and `TimesChanged` keeping the shown line (no
    `Loading…`); no buttons, never takes focus. `StateComponent.OnDisposing` is the hook to drop extra subscriptions. A click
    goes to `Main.OpenAppointment` (save first, else `Discard changes to <name>?`; Discard drops the note and shows the page
    over the placeholder): `Page.Appointment` shows `AppointmentPanel` (`section.page.appointment-page`, `h2` title, icon-only × `button.close`, `h3 Notes` above every state, the list `aria-labelledby` it;
    the outline is hidden, opening any note closes it). Same-titled appointments (ordinal) on the day share one page (a
    second click keeps it, no reload). It lists `DesktopState.AppointmentPage(title, day)` (`MeetingPrep.Related(...,
    max: null)` over the title and the union of the attendees of those appointments; `Sync`, `NoteTags()`, `List()`
    under `indexGate`; any failure gives `IndexUnreadable`, red `Can't read the related notes.`) as `note-button`s
    (title, full name in `title`, grey `.matches`) through `RequestOpen`; with none, `button.create-note-link`
    `Create a new note` runs the `/meeting` steps (`Main.CreateAppointmentNote`, the first such appointment re-read
    through `DayAppointments`) and opens the meeting note with accent `Created <name>`.
    **Meeting todos** (job 287; the desktop has no `/current`, so the appointment page stands in for it and for the
    appointment's menu; `context current` is unchanged): `MeetingTodosPanel` (`section.meeting-todos`, `h3 Todos`) sits
    on the appointment page below the topics and, in `Main`, above the editor of a note named `Meetings/<part>/yyyy-MM-dd`
    (`DesktopState.MeetingNoteDay`). It reads `DesktopState.MeetingAppointments(day, title, noteName)` on `Task.Run`
    (the day's appointments with the title, or those `MeetingNote.IsNoteOf` the note; none renders nothing) and
    `MeetingTodos(appointments)` (the snapshot's open todos whose `Meeting` `Matches` one, `List()` order). The first
    appointment without a usable Uid gives grey `This appointment can't hold todos.` and no controls. Else, in keyboard
    order: `input.meeting-todo-text` (placeholder and `aria-label` `Todo for this meeting`, max 200) and `Add`
    (`Actions.AddMeetingTodo(first, text)`: due the first's date, tied to it), `select.tie-todo` (`Tie an open todo…`,
    then the untied open todos; a choice runs `Actions.TieTodo(id, first)`), then each tied todo's checkbox (the usual
    `Mark this todo done?` dialog) and `button.push-todo` `Push to next meeting`: `DesktopState.NextMeeting(todo)`
    (`MeetingLink.NextOccurrence` over files, the feed cache, never a download, and Outlook when on) and `TieTodo` to
    it, accent `Moved to yyyy-MM-dd.`; with none, `No next meeting found. Pick another appointment:` and
    `button.pick-meeting`s (`yyyy-MM-dd HH:mm Title`) of `UpcomingAppointments(today)` (today..+13, with a Uid) minus the
    current occurrence, or grey `No other appointments in the next 14 days.` A todo done or gone meanwhile is red
    `The todo list changed. Nothing was moved.` (`TieTodo` writes nothing). `TodoPanel` shows a tied todo's
    `MeetingLink.Title` as grey `span.todo-meeting` (cut, full title in `title`) and grey `Meeting cancelled or moved`
    for ids in `DesktopSnapshot.MeetingHints`: open tied todos due today..+100 whose occurrence is `Missing`, an
    `outlook:` Uid only from an `Ok` Outlook read of the day, any other from the files plus the cached feed when no
    calendar file is unreadable (without a cache, only a Uid the files have); never a download.
    `NoteEditor` renders none: it reloads the list after the mount, after every `Done` save (auto or explicit),
    `ReloadAsync` and `ShowSavedAsync` and reports each through `OnOutline` (empty when disposed or locked); `Main` keeps
    the current editor's latest list (each editor's callback carries the generation it was rendered with, so a report
    from an older editor is ignored; its receiver is no component, so only a changed list renders `Main`; opening
    another note, closing or locking clears it), hides it while a page or the
    search panel is shown, and `OnJump` calls `NoteEditor.JumpToIndexAsync`. An editor that didn't start, an unreadable
    note or failing `headings` shows none, and a locked sensitive note's outline goes with its editor (its headings are only ever read from the unlocked editor, never indexed).
    **Outlook** (job 217, Windows with classic Outlook only): `IOutlookSource` (`Availability()` `Unsupported`/
    `NotInstalled`/`Available`; `Appointments(day, zone)` gives `OutlookRead<T>` with a
    kind `Ok`/`NotRunning`/`Denied`/`Failed` and the items, never an exception text; `RecentMail(sinceUtc, max)` for
    the Mail digest, below. Job 223 removed the old mail panel. The Mail digest's read is the only Inbox read: opt-in,
    only on a click of Read mail, private items skipped, `Body` capped). `Program.cs` is the only file naming `OutlookComSource` (Windows; elsewhere `UnsupportedOutlookSource`),
    and no test does (tests use `FakeOutlookSource`: availability, kind and items per call, call counts, `Throws`, a
    `ManualResetEventSlim` gate released on dispose). `OutlookComSource` is late-bound (`dynamic`, no interop package),
    attaches only through `ole32!CLSIDFromProgID` + `oleaut32!GetActiveObject` (never `Activator.CreateInstance`, never
    starts Outlook), reads only `Sensitivity`, `Subject`, `Location`, `Start`, `End`, `AllDayEvent` of the
    default Calendar (9; `Sort("[Start]")`, `IncludeRecurrences`, `Restrict` built only from the day in current-culture
    `g`), walks `GetFirst`/`GetNext` (never `Count`; 500 items), converts machine time to the
    calendar zone and `FinalReleaseComObject`s every COM object; it writes nothing (no `.Save(`/`.Send(`/`.Delete(`/
    `.Move(`/`.Display(`) and reads no `HTMLBody`/`Recipients`/`RequiredAttendees`/`SenderEmailAddress` (and `Body` only
    in the Mail digest's capped read), and has no exemption from any other source scan. Private items (job 227, always on, no setting): `ReadAppointment` reads
    `Sensitivity` first and skips the item, before reading anything else of it, unless `OutlookSensitivity.IsPrivate`
    sees a number equal to 0 (Normal) or 1 (Personal); Private (2), Confidential (3), any other or unreadable value is
    hidden (fails closed; `OutlookSensitivity.cs`, `OutlookSensitivityTests`). On = `SettingsStore.ReadOutlook()` (exactly `"outlook":{"enabled":true}`;
    anything else or a broken file is off) and `Available`; off asks the source nothing. `DesktopState` caches each
    day's appointments (one `OutlookCall` with its start time under `outlookGate`; a running call is shared,
    a finished one kept `OutlookCacheAge` = 5 min by the UTC clock), waits at most `OutlookTimeout` (10 s, ctor
    parameter) and maps the result to `OutlookStatus` (`Off`, `Ok`, `NotRunning`, `Denied`, `Failed` incl. a throwing
    call, `TimedOut`). Outlook's appointments join the feed's through `CurrentView.Build(..., extra)` /
    `CalendarStore.Day(d, extra)` in the snapshot, `DayAppointments` and `AppointmentPage`; `DayAppointmentsPanel`
    shows `p.outlook-status` (`OutlookStatusLine`: grey `Outlook isn't running. Start it to see its
    appointments.`, red `Can't read Outlook (access denied).`/`(error).`/`(timed out).`); it is the last Outlook panel in
    `aside.todos-region` (the Mail panel was removed in job 223). Settings: `h3` `Outlook` between the calendar and the key (grey `Outlook is only
    available on Windows.` / `Classic Outlook isn't installed. The new Outlook can't be read; use a calendar link
    instead.`, else the checkbox `input.outlook-enabled` `Read calendar from Outlook` and grey `Outlook may ask
    whether Context may access it.`); `DesktopActions.SetOutlookEnabled(on)` (`SaveOutlook`, then `ForgetOutlook`; Done
    `Outlook on.`/`Outlook off.`, Refused when turning on without `Available`, Failed `Can't read settings.json. Nothing
    was saved.` for a broken file). Nothing from Outlook is stored on disk; the CLI, `review` and the briefing don't
    read it.
    **Mail digest** (job 286, opt-in, no model): `MailDigest.cs` (Core, pure): `MailMessage(Subject, Sender, Received
    UTC, MessageClass, Body)`, `MailSuggestion(Kind, Text, Due?, Title?, Source)` with `MailSuggestionKind`
    `Todo`/`FollowUp`/`MeetingNote`, and `MailDigest.Suggest(messages, today, hidden)`: newest mail first; a
    `MessageClass` starting with `IPM.Schedule.Meeting.Request` (ordinal, any case) gives only a MeetingNote whose
    `TitleFor(subject)` turns path characters and controls into spaces, collapses and trims, cuts to 100 (never inside
    a surrogate pair), strips leading/trailing dots and spaces and a trailing `.md`, else `Mail note`; any other mail
    gives up to 3 Todos for sentences (split after `.`/`!`/`?` before whitespace or the line end) starting with
    please/can you/could you/bitte/kannst du/könntest du (then a non-letter), controls removed, cut to 200, plus one
    FollowUp `Reply to <Sender>: <Subject>` (same cleaning, due today + 2) when a body line ends in `?`; deduped by
    kind and text, at most 50 (`MailDigestTests`). `OutlookComSource.RecentMail` reads the Inbox (6) only: `Restrict`
    on `[ReceivedTime]` from the date alone, `Sort("[ReceivedTime]", true)`, `GetFirst`/`GetNext` up to 200; `ReadMail`
    reads `Sensitivity` first and skips private items, then only `Subject`, `SenderName`, `ReceivedTime`,
    `MessageClass` and `Body` cut to `MailDigest.MaxBodyLength` (4000). On = `SettingsStore.ReadMailDigest()`
    (`"mailDigest":{"enabled":true}`), the Outlook setting and `Available` (`DesktopState.MailDigestOn()`); Settings
    has the checkbox `input.mail-digest-enabled` `Suggest todos from Outlook mail` under the Outlook checkbox
    (`DesktopActions.SetMailDigestEnabled`, Done `Mail suggestions on.`/`off.`, Refused without `Available`).
    `MailDigestPanel` (`Page.MailDigest`, palette `/digest`): grey `Turn on mail suggestions in Settings.` when off;
    else `button.read-mail` `Read mail` is the only thing that reads (`DesktopState.MailDigest()`: 7 days, 200 mails,
    `OutlookTimeout`, never cached, never on open or on a state change), `Reading mail…` meanwhile, then
    `OutlookStatusLine.MailText` (NotRunning `Outlook isn't running. Start it to see its mail.`) or rows
    `li.digest-row` (kind, text as one line, grey `Sender · yyyy-MM-dd HH:mm`, `button.digest-accept`/`.digest-dismiss`;
    focus moves to the next row's Accept). `DesktopActions.AcceptSuggestion`: Todo `AddTodo(text, today)`, FollowUp
    `AddTodo(text, due)`, MeetingNote `CreateWithText(Title, "# Title\n\nFrom mail by <Sender>, <yyyy-MM-dd>.\n")`
    (Refused/Invalid with the `ValidateTitle` text, Refused/Exists with `ExistsText(Title)`); Done hides the row,
    Refused/Failed keep it with `p.digest-result`. Accepted and dismissed suggestions are hidden for the session in
    memory only (`DesktopState.HideMailSuggestion`); no mail text is written, logged or cached in files, and the CLI,
    `review` and the briefing never see it (`MailDigestPanelTests`, `MailDigestActionsTests`).
    **Smart paste** (job 244): `editor/src/paste.js` (DOM-free): `tableText` (CRLF/CR to `\n`, one trailing `\n`
    dropped, 2-500 lines, equal tabs 1-49, no `|` in a cell; trimmed cells, header, `---` row), `loneLink` (trimmed,
    one `http(s)` URL with a host, no whitespace, at most 2048), `smartPaste(state, text, plain)` (null for plain, a
    non-paragraph or code; a table replaces the selection as its own paragraph; a link goes in as unmarked text with
    meta `linkMeta`), `linkTitleTransaction` (only while the mapped range still holds exactly the unmarked URL and the
    block saves and parses back with the same link runs; `closeHistory`, one undo step), test hooks `pastedMarkdown`
    and `titledMarkdown` (`SmartPasteBundleTests`). `view.js` `handlePaste` treats a paste as plain while Shift is held
    (own keydown/keyup tracking, ProseMirror's rule incl. key 45), records each pasted link (`maxPendingLinks` 20,
    mapped on every transaction) and calls `OnPasteLink(url)`; `setLinkTitle(element, url, title)` applies a title.
    `markdown.js` escapes no text at all (job 290; `RoundTrip/lossless/table.md`, `escaped-pipe.md`). Core `PageTitle.cs`:
    `IPageTitleSource`, `PageTitleFetcher(handler, timeout)` (one GET, `Accept` HTML, 2xx, final URI http(s), media type
    absent/`text/html`/`application/xhtml+xml`, first `MaxBodyBytes` 256 KiB, declared charset else UTF-8, first
    `<title>` HTML-decoded, bidi controls removed, control/whitespace runs one space, cut to 200; user info or a padded URL refused; never
    throws, never logs). `SettingsStore.ReadFetchTitles` (exactly `"paste":{"fetchTitles":true}`, a broken file
    throws) / `SaveFetchTitles` (other keys kept). `DesktopState.FetchTitlesSetting()` (false on error) and
    `PageTitle(url, token)` (null when off, cancelled or no source); `Program.cs` passes its own `SocketsHttpHandler`
    (`UseCookies = false`, no credentials, 5 redirects) with a 3 s timeout. `NoteEditor` handles `LinkPasted` only for a
    plain mounted note (`LastTitleFetch` for tests) and cancels pending fetches on reload, `ShowSavedAsync` and dispose.
    Settings `h3` `Pasting` (after Outlook): `input.fetch-titles` `Fetch page titles for pasted links`, grey hint;
    `DesktopActions.SetFetchTitles(on)`: accent `Page titles on.`/`Page titles off.`, else red `Can't save
    settings.json.` with the box back. Tests use `FakePageTitles` and `FakeHttpHandler` (`RedirectTo`). Manual check:
    Cmd+Shift+V in WKWebView (README).
    **Screenshots into the inbox** (job 260): Core `ScreenshotInbox.Add(store, attachments, fileName, bytes, now)`
    (size, then `CleanName` + `Sniff` must give a PNG/JPEG/GIF/WebP whose extension matches, then a sensitive inbox is
    refused by name, a missing inbox is created as `# inbox\n`, `NoteAttachments.Add` stores the image, and
    `Inbox.Capture` appends `- yyyy-MM-dd HH:mm Screenshot ![<name>](.attachments/<name>)`; when the capture fails the
    stored file is deleted again, never through a link). `ScreenshotInbox.HotkeyName(now)` is
    `screenshot-yyyyMMdd-HHmmss.png`. Settings: `"screenshots":{"folder":<absolute path>}` through
    `SettingsStore.ReadScreenshotFolder` (null when missing, a broken shape throws) / `SaveScreenshotFolder` (invalid
    shape: `ArgumentException`) / `RemoveScreenshotFolder` (false and no write when not set), every other key kept.
    `DesktopActions.CaptureScreenshot()` (sensitive inbox refused before the area picker, cancel writes and shows
    nothing), `ImportScreenshot(path, lastTry)` (only a direct, non-dot, non-link image child of the saved folder;
    others are Refused/`Missing` and quiet; the source stays; `Locked`/`Unreadable`/`Busy` show only on the last try),
    `SetScreenshotFolder` (absolute, at most 1024, existing, outside the Context folder; each refusal its own text) and
    `ClearScreenshotFolder`; every result goes to the notifier as `Screenshot added to inbox.` or
    `Screenshot not saved: <reason>.` `ScreenshotFolderWatcher` (one non-recursive `FileSystemWatcher`, Created and
    Renamed, 300 ms debounce, one file at a time, each path once, 1 + `MaxRetries` 5 tries with the `RetryDelay` hook,
    `Processed` for tests) follows `DesktopState.Changed`, so saving or removing the folder restarts or stops it; a
    missing folder is never created. `ScreenshotHotkey` runs one capture per press (a press during a capture is
    ignored). The real parts are macOS only and picked in `Program.cs`: `MacScreenCapture` (`/usr/sbin/screencapture
    -i -x <root>/.tmp/screenshot-<guid>.png`, no shell, the temp file removed) and `MacGlobalHotkey` (Carbon
    `RegisterEventHotKey`, Control+Option+Shift+S); other systems get neither. Settings `h3` `Screenshots` (after
    Pasting): the hotkey line (`DesktopState.IsMac`, injected by `DesktopFixture.IsMac`), the folder status (grey
    `Folder: <path>`, red `Folder not found.`, red `Can't read the screenshot folder setting.`), `input.screenshot-folder-field`,
    `Save folder`, `Remove folder`. Tests: `ScreenshotInboxTests`, `ScreenshotActionsTests`,
    `ScreenshotFolderWatcherTests` (bounded `SpinWait` on `Processed`), with `FakeScreenCapture` and `FakeGlobalHotkey`.
    **Command bar** (job 282, macOS only): a second global hotkey, Control+Option+Space (`GlobalHotkeys`: key codes,
    modifiers and the ids 1 screenshot / 2 bar; `MacGlobalHotkey(keyCode, modifiers, id)` answers only events whose
    `GetEventParameter` id is its own), opens `PhotinoCommandBarWindow` (a chromeless, topmost 640x300 child window
    serving only `wwwroot/commandbar/commandbar.{html,js,css}` through its `ctxbar:` scheme; CSP `'self'`; the page sets
    text only through `textContent`). `CommandBarHotkey` shows the bar when none is open and focuses it otherwise.
    Each bar gets a `CommandBarHost`: JSON messages `complete`/`run`/`close` (over 4 KiB, malformed or unknown: no
    answer), answers `suggestions`/`result`/`close`/`focus`; one run at a time; an `ok` result closes after
    `CloseDelay` (1.5 s, through the injected `delay`). `CommandBarRouter` (pure): `Route` sends `todo`/`note`/`capture`
    to Context, everything else to Times as separate arguments (one leading `times`/`tm` dropped; in `cp`/`checkpoint`
    a quicklist work item becomes `#<position>`); `Suggest` ranks command names with `CommandMatcher` and lists
    quicklist rows after `cp `. Core `TimesProjects.Read(timesFolder)` reads Times' `projects.json` (read-only, 64 KiB,
    any bad row: empty). `TimesExecutable.Find` looks in `~/.dotnet/tools` then `PATH` (`tm`, `tm.exe` on Windows);
    `TimesProcessRunner` runs it with `ArgumentList`, stdin closed, `NO_COLOR=1`, 64 KiB per stream, killed after 10 s.
    Context lines go through `CommandBarContext` (a singleton) to `Main.RunFromBar`, which Main registers in
    `OnInitialized` and unregisters on dispose (the one Actions caller not bound to an event attribute, allowed by
    `ActionCallProblems`); `todo` uses `TodoCommand.TryParse` (shared with the palette's `/todo`), `note` calls
    `DesktopActions.NoteToday` (`- HH:mm <text>` at the end of today's daily note, 1-1000 chars, 3 retries, sensitive
    refused). Settings `h3` `Command bar` (after Screenshots): the hotkey line and, when `DesktopState.TimesCommandFound`
    is false (`DesktopFixture.TimesCommand`), a grey "tm not found" line. Tests: `CommandBarRouterTests`,
    `CommandBarHostTests`, `CommandBarHotkeyTests`, `CommandBarMainTests`, `NoteTodayActionsTests`,
    `TimesExecutableTests`, `TimesProcessRunnerTests`, `TimesProjectsTests`.
    **Crash log** (job 237, `CrashLog.cs`, desktop only): `CrashLog(root, utcNow, version, os, home)` writes into
    `<root>/.logs/` (a dot folder, so `List()` and `DataWatcher` ignore it). `Write(source, e)` writes
    `crash-yyyyMMdd-HHmmss-fff.log` (UTC; `-2`..`-99` on a clash, `FileMode.CreateNew`) holding only the UTC time, the
    version, the OS, the source and per exception (depth first, `InnerException` and every
    `AggregateException.InnerExceptions` entry, at most `MaxDepth` 10 levels) the full type name, the message and the
    stack trace, each through `SafeText.Clean`, the home folder replaced by `~` (ignoring case) and cut to `MaxText`
    (2000; only the home folder becomes `~`, other absolute paths in messages and stacks stay); never `Data`, environment, arguments or a machine/user field; below `MaxBytes` (64 KiB, later entries left
    out). It then keeps the newest `MaxLogs` (10) regular files named exactly like a log (no link; `File.Delete`, its only
    writing call besides `File.WriteAllText` and the `CreateNew` stream) and returns the path, or `null`; it never throws.
    The session file `.logs/session` holds `running <ISO-8601 UTC start>` or `clean <start>` (written in place).
    `Begin()` reads it (missing, unreadable, over 200 bytes or malformed: no session, no hint), returns a
    `CrashHint(LogPath, Folder)` for the newest log whose name time is at or after the previous start, or after a
    `running` session with none writes an `unclean-exit` log (`No exception was caught; the app was killed or the web
    view stopped. Previous start: <S>.`) and returns that; then writes `running <now>`, where now is read after that
    write and is at least 1 ms after the newest log's name time (so a shown log never shows again on the next start).
    `End()` writes `clean <start>`. Nothing is written through a link: when `.logs` is a link, a reparse point or not a
    folder (`CrashLog.IsPlainFolder`), or `session` is a link, a folder or a reparse point, the write is skipped.
    `Program.cs` builds it right after `VelopackApp.Build().Run()` (`NoteStore.DefaultFolder()`, OS description +
    architecture, `StartupUpdate.CurrentVersion`, the user profile), hooks `AppDomain.UnhandledException` (`unhandled`)
    and `TaskScheduler.UnobservedTaskException` (`unobserved-task`, then `SetObserved()`), calls `Begin()` only through
    `DesktopStartup.Begin` once it holds `<root>/.desktop/running.lock` (job 315: a second copy never reads or writes
    `session`), registers the
    log and the hint (when there is one) as singletons, and calls `End()` after `app.Run()`. `App.razor` wraps `<Main />`
    in `CrashBoundary` (`ErrorBoundaryBase`; `OnErrorAsync` writes source `component`; shows red `p.crash-boundary`
    `Something went wrong. Crash log: <path>. Close and reopen Context.`). `Main` renders `CrashHintBar` first: with a
    hint `.crash-hint` (fixed at the top, theme colours) reads `Context closed unexpectedly last time. Crash log: <path>`
    (path in `title`) with `Open folder` (`DesktopActions.OpenCrashFolder(hint)`, the launcher with `CrashHint.Folder`,
    only when it is a plain folder at click time; otherwise, or on a throw, red `Can't open the folder.`) and `Dismiss`. Both components get the optional services through
    `IServiceProvider.GetService`. `CrashLog.cs` names no `NoteStore`, `DesktopActions` or `Console.` (`DesktopSourceTests`).
    Not covered: a web-view render crash while the app keeps running (only the next start's `unclean-exit` sees it).
    **Update:** `Program.Main` starts with `VelopackApp.Build().Run()` (Velopack's install/update hooks), and before
    the window opens `StartupUpdate(IUpdateSource).Start()` runs the silent update on a pool thread: it returns at once
    and never throws. Not `IsInstalled`: nothing is called. Otherwise `CheckAsync`; when a newer version is found,
    `DownloadAsync` once, then `ApplyOnExit` once (put in place after the app closes, no restart; Velopack's updater waits
    only 60 s for the app to close, and after that `VelopackApp.Build().Run()` applies the pending package at the
    next start and restarts into it). Any exception or
    cancellation is swallowed with no output; the next start tries again. `StartupUpdate.IncludePrereleases(version)`
    is true only for a SemVer version with a prerelease label (`StartupUpdate.CurrentVersion` is the assembly's
    informational version). `VelopackUpdateSource` is the one real source: Velopack's `UpdateManager` over
    `GithubSource` with the fixed repository URL, no access token (so it can never list a draft release), no explicit channel and no downgrade; installed
    means Windows, installed by `Setup.exe` and not the portable copy. There is no update UI.
    `wwwroot/index.html` (CSP meta tag, `img-src 'self' blob:` for attachment images, `frame-src 'self'` for the Mermaid frame (job 263), only `_framework/blazor.webview.js` and `editor/editor.js`), `wwwroot/app.css`
    and `wwwroot/editor/editor.css`.
  - **Desktop rules:** the Core writing calls (`Create`, `Save`, `SaveIfUnchanged`, `Add`, `Complete` (also `NoteTodos.Complete`), `Set`, `Clear`, `Delete`,
    `Rename`, `CreateChild`, `Move`, `MoveWithNote`, `RemoveUnlinked` (`NoteAttachments`), `ClearCache`, `Capture` (`Inbox.Capture`), `MarkNotified` (named only in `TodoStore.cs` and `DesktopActions.cs`), `Snooze` (`InboxTriage.Snooze`; `InboxTriage.Remove` is checked on its own, since components remove from their own lists), `Ensure` (`DailyNote.Ensure`, `MeetingNote.Ensure`, `Briefing.Ensure`), the note history (`Keep`, `RemoveHistory`, `RestoreVersion`; only the call `Actions.RestoreVersion(` in a `.razor` file is allowed), `CreateWithText` (`CreateNoteFromTemplate`), the day's end (`CloseDay`, `RefreshWorked`, `RefreshStandup`, `MoveTodos`, `AutoClose`, `MoveDue`, `SaveDayEnd`), and for sensitive notes `Seal`, `AddRecipient`, `DeleteSensitive`, `CreateMachineKey`,
    `StoreRecoveryPublicKey`) appear only in `DesktopActions.cs` (exemptions: `DotNetObjectReference.Create(` once in
    `EditorInterop.cs`, `ECDiffieHellman.Create(` in `SealedNote.cs`, and `WindowsKeyStore.cs`, whose .NET calls change
    the certificate store and which names no Core store, `File.`, `Directory.` or `Path.`; the reads still write
    only through `TagIndex.Sync` and `CalendarFeed.Fetch`, as on the command line). `DesktopActions` is named only by
    `.razor` files, `Program.cs` and `StateComponent`, never by `DesktopState`, `DataWatcher` or `DesktopSnapshot`, so
    nothing writes on render, refresh, the watcher or the tick. The named exception (jobs 171 and 172): `Program.cs`'s
    `AutoWrites` (once at start and in the day-end timer) calls `DesktopActions.AutoDayEnd()` and then
    `DesktopActions.AutoBriefing()`, (job 178) `DesktopActions.AutoReminders()` runs once at start before
    `AutoWrites` and on its own timer every `ReminderInterval` (30 s, first after half of it, so it doesn't collide
    with the day-end timer), and (job 216) `DesktopActions.AutoOpenToday()` runs once at start after the first
    `AutoReminders()` and before the first `AutoWrites` (it creates today's daily note when missing); these are the
    only methods `Program.cs` calls on the actions and named by no other `.cs` or `.razor` file, so the automatic
    writes (the day's end, the morning briefing and today's note at start) still write only through
    `DesktopActions.cs`; `Briefing.Ensure`, `DesktopState.OfferBriefing` and `DesktopState.OfferStartNote` are called
    only there. In `.razor` files every `@code` method that calls
    `Actions.` is bound by an event attribute in the same file (`@on<event>=` on an element or an `On<Event>=`
    callback parameter on a component), is no lifecycle method (`OnInitialized*`, `OnParametersSet*`,
    `OnAfterRender*`, `SetParametersAsync`, `BuildRenderTree`) and is called by no other method; no `.cs` file calls
    `Actions.`. Destructive actions take a `Confirmation`, and `new Confirmation(` appears only in
    `ConfirmDialog.razor`.
    `Velopack` is named only in `VelopackUpdateSource.cs` and `Program.cs`; the update classes (`IUpdateSource.cs`,
    `StartupUpdate.cs`, `VelopackUpdateSource.cs`) never mention `NoteStore`, `DefaultFolder`, `DesktopActions`,
    `Environment.GetEnvironmentVariable`, `args` or `Console.`, so an update never touches the data folder; no test
    references `VelopackUpdateSource` (tests use `FakeUpdateSource`, never the network).
    **Launcher rule:** `Process` / `System.Diagnostics` appear only in `ShellNoteLauncher.cs`, (job 178)
    `SystemNotifier.cs`, (job 260) `MacScreenCapture.cs` and (job 282) `TimesProcessRunner.cs`, each named only in itself
    and in `Program.cs` (the runner also in its own `TimesProcessRunnerTests`, Unix-only temp `tm` scripts; every other
    test uses `FakeTimesRunner`), and (job 280)
    outside the desktop only in Core's `GitCommitLog.cs` and the command line's `ProcessDesktopLauncher.cs` (both
    `ArgumentList` only, never a shell; `WorkItemSourceTests`); no test names `ProcessDesktopLauncher`; no test
    names `MacScreenCapture` or `MacGlobalHotkey` (tests use `FakeScreenCapture` and `FakeGlobalHotkey`); no test names `SystemNotifier` (tests use
    `FakeNotifier`, which records `(title, body)` and can throw), and the notifier passes the text only as arguments or
    environment values. The only path `ShellNoteLauncher` ever gets is a checked note or attachment path (a note from `List()`, or an
    attachment PDF `NoteAttachments.TryResolve` accepted), and the only folder it gets is the fixed crash folder
    `<root>/.logs` (`CrashHint.Folder`, job 237); no user text becomes a path or an argument; no test references `ShellNoteLauncher` (tests use `FakeNoteLauncher`, which records paths and can throw).
    Outside text goes through `DesktopText.OneLine` and Blazor's default encoding: never `MarkupString`,
    `AddMarkupContent` or `@((` raw markup; no `style=`, `<a `, `<form` or `<script` in `.razor` files; every
    `<button` has `type="button"`; no plain HTML event attributes (`on<event>=` without `@`); only `EditorInterop.cs`
    mentions `IJSRuntime`/`JSRuntime`/`JSInvokable`, and its `[JSInvokable]` methods only raise events (never `Actions.`).
    The editor sources (`editor/src/*.js`) use no `innerHTML`, `outerHTML`, `insertAdjacentHTML`, `document.write`,
    `eval` or `new Function`. Every `<input>` is `type="checkbox"`, `"text"`, `"date"`, `"time"`, `"search"` or `"password"`, without `@bind`,
    and there is no `<textarea>`. No CDN or external fonts; error
    lines are fixed texts, never exception messages. The calendar link is a bearer secret: in `Context.Desktop` the type `CalendarLink` is
    named only in `Program.cs`, `DesktopState.cs` and `DesktopActions.cs`; no file calls `.Get()` on it or reads
    `.AbsoluteUri`, `.OriginalString`, `.PathAndQuery` or `.Query`; `CalendarLinkView.Run` is named only in
    `DesktopActions.cs`, and components name only `CalendarLinkView`/`CalendarLinkViewKind`, so the page sees the
    mask alone. No `.razor`, `.cs`, `index.html` or editor source names `localStorage`, `sessionStorage`,
    `indexedDB`, `document.cookie`, `NavigationManager` or `NavigateTo`, and `SettingsPanel.razor`,
    `DesktopActions.cs` and `DesktopState.cs` name no `ILogger`, `Console.`, `Debug.` or `Trace.`.
    Colours by CSS class (`accent`, `grey`, `red`, dates `overdue`/`today`/`later`); long lines
    cut by CSS with the full text in `title`.
  - `tests/Context.Tests/`: references both projects; test files use the file-scoped `namespace Context.Tests;`;
    one test class per production class (Core and `Cli` alike), `CiWorkflowTests.cs`, `ReleaseWorkflowTests.cs` and
    `CoreDependencyTests.cs` and `StrongNameTests.cs` (the workflows and their four jobs, the release scripts and the tool packaging, with
    `WorkflowYaml.cs` and its `WorkflowYamlTests.cs`; the architecture test: no P/Invoke and no sealing code in Core,
    and no terminal UI assembly, package or source mention in any project; the strong-name test: the `Context.snk` key on
    Core, `Context` and the tests, keyed `InternalsVisibleTo`, a name-only `Context.Desktop` dynamic assembly refused, no
    tracked private key), plus shared helpers such as
    `RemovedFeature.cs` and `RemovedConsole.cs` (the names of the removed whole-folder protection and of the removed
    terminal console, spelled in pieces; `RemovedFeatureSourceTests.cs` fails when `src/`, `tests/` or this file
    names either again) and `FakeHttpHandler.cs` (canned HTTP responses). The view tests (`CurrentViewTests`, `ReviewViewTests`, `DoneLogTests` (the done log), `DoneLogViewTests` (its weeks), `TagsViewTests`, `LinksViewTests`, `FindViewTests`, `SearchViewTests`, `DecisionsViewTests`, `CalendarLinkViewTests`) check the view data field by field. Note, todo, tag and calendar tests use their
    own temp folder (deleted in `Dispose`), never the real app data folder.
  - `tests/Context.Desktop.Tests/`: xUnit + bunit, references `Context.Desktop` and `Context.Core`; one test class per
    component or class, plus `EditorBundleTests` (Jint on the committed `editor.js` against `RoundTrip/lossless` and
    `RoundTrip/lossy` fixtures, compared with the C# parsers; `.gitattributes` keeps their bytes), `EditorJs` (bunit
    JSInterop setups for the page editor), `EditingFlowTests` and `TodoEntryTests`, `StartupUpdateTests` (with `FakeUpdateSource`), `NoteActionsTests` (note actions, with `FakeNoteLauncher`), `LinkCreateActionsTests` (`CreateLinkedNote`), `CaptureActionsTests` (`CaptureText`), `TriageActionsTests` (the triage actions) and `TriagePanelTests` (the panel, `/triage` and the `Triage` button through `Main`), `MailDigestPanelTests` (the Mail digest page and `/digest` through `Main`, with `FakeOutlookSource`) and `MailDigestActionsTests` (`AcceptSuggestion`, `SetMailDigestEnabled`), `NoteTransferActionsTests` (`ImportNote`, `ExportNote`; sources and targets in `DesktopFixture.Outside`, with `FakeFileDialog` for the panels), `ShareActionsTests` (`ShareNote`) and `ShareDialogTests` (Share through `Main`), `SettingsPanelTests` (with the link-secrecy marker test through `Main`), `PeoplePanelTests`, `ReviewPanelTests`, `DoneLogActionsTests` (the done-log append on completion) and `DonePanelTests` (the Done log page and `/donelog` through `Main`), `CalendarLinkActionsTests`, `DayEndActionsTests` (`EndDay`, `AutoDayEnd`; Times files in `DesktopFixture.TimesFolder`), `StandupActionsTests` (`WriteStandup`), `StartNoteActionsTests` (`AutoOpenToday`), `BriefingActionsTests` (`AutoBriefing`, `OpenBriefing`), `ReminderActionsTests` (`AutoReminders`, with `FakeNotifier`) and `TimedTodoTests` (the palette's timed `/todo`), `KeyDetailsTests`, `DesktopStrongNameTests` (the `Context.snk` key on `Context.Desktop` and its tests, keyed `InternalsVisibleTo`), `DesktopDependencyTests` (no terminal UI library, no Jint
    outside tests, exact npm versions, Velopack only in `Context.Desktop`), `DesktopSourceTests` (raw markup, risky elements,
    CSP, write sites, event-handler-only actions, button types, checkbox-only inputs, JS interop, the launcher rule, the calendar-link, browser-storage and logging rules, the transfer calls and no file system class in `.razor`), `DesktopFixture` (temp folder, deleted under the state's `indexGate` so a panel's index read never races it; injected
    day/clocks, `FakeHttpHandler` linked from `Context.Tests`, a `BunitContext` serving the state and the actions),
    `FileSnapshot` (bytes and last-write times of a folder, to prove nothing changed), `RepoFiles` and `WaitTimeoutSourceTests` (the wait rule). A `WaitForAssertion` that waits for work behind `Task.Run` (such as
    every wait in `SensitiveFlowTests`, which runs real ECDH and AES-GCM) passes an explicit, bounded timeout from a named
    constant in its class, and no test ever changes `BunitContext.DefaultWaitTimeout`. The guard finds every `.cs` file under `tests/Context.Desktop.Tests/` that calls `WaitForAssertion`, `WaitForState` or `WaitForElement`; each has exactly one `Wait` constant (10-30 s) passed as the last argument of every wait; a new test file needs no registration. Any component's render re-parses the whole rendered markup, so a test
    that compares elements or clicks right after opening a note first waits until the page's own `Task.Run` loads
    have rendered (job 175: `MainTests.OpenFull` waits until `.links-panel` shows no `Loading…`). A `WaitForAssertion`/`WaitForState` re-checks only after a render, so a wait on state that is not rendered (set off the renderer with no render after it) awaits a task with the class's `Wait` instead (job 204: the window close completes `DesktopFixture.Closed`, and no wait lambda reads `CloseCalls`, checked by `WaitTimeoutSourceTests`; `DesktopFixture.CloseAfter` holds a close until a test lets it go). A test that compares `RenderCount` after an action that refreshes the state first awaits `DesktopState.ReloadsSettled()` with its `Wait` (job 221: `StateComponent` counts every `Changed` reload through `BeginReload`/`EndReload`), then an empty `cut.InvokeAsync`. A test that clicks and then asserts the click's effect uses `await ClickAsync` on an element found right before the click, never the fire-and-forget `Click()` (job 222: `ReviewPanelTests`).
- **Style:**
  - `Cli` writes plain text through injected `TextWriter`s (stdout, stderr) and mentions no `Console.` (checked
    by `CliTests`); only `Program.cs` names `Console`. The desktop shows text through Blazor components only.
  - Text from disk or other outside sources (note text and names, OS exception messages, later email
    and calendar data) goes through `SafeText.Clean` before it is shown: it keeps `\n`, turns `\r\n`
    into `\n` and `\t` into 4 spaces, and replaces every other C0/DEL/C1 control with `�` (U+FFFD).
    One-line fields also turn `\n` into a space (`Cli`'s `OneLine`, `DesktopText.OneLine`). Never write such
    text to the terminal or the page raw.
  - File-scoped namespaces, `internal sealed` unless a type must be public.
  - No primary constructors anywhere (classes, structs or positional records). Write a
    conventional constructor that assigns to fields or properties.
  - Private fields are camelCase **without** a leading underscore (`stdout`, not `_stdout`).
    `this.` only where a field and a constructor parameter share a name.
  - No `var`: always write the explicit type. Braces on every `if`/`else`/`for`/`foreach`/`while`
    body, even single lines.
  - `.editorconfig` holds these rules. The build enforces it (`EnforceCodeStyleInBuild`), so a
    style break is a build warning, and builds must stay at 0 warnings.
  - `Run` returns a process exit code: 0 for success, non-zero for errors.
  - No new NuGet packages without a reason in the spec. The approver escalates any new dependency.
- **Git:** `develop` is the working branch: factory work branches off it as `factory/<job-id>` and is merged back with `--no-ff`. `main` holds releases only and is pushed to only for a new release.
  `.gitignore` is the standard `dotnet new gitignore`.
