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
    note that couldn't be read so it is retried), `note_tags(note, tag)`, `note_links(note, target, heading)` (job 205: `heading` is the text after the first `#` or NULL; primary key stays `(note, target)`; a database without the column gets it through `ALTER TABLE` on every open, before any query), `note_people(note, person, spelling)`, `note_decisions(note, line, text)` (job 180, primary key `(note, line)`; one row per `DecisionParser` line, written and pruned with the note's other rows), `note_todos(note, line, due, text, raw)` (job 201, primary key `(note, line)`; one row per open `NoteTodoParser` line, `due` `yyyy-MM-dd` or NULL, written and pruned like `note_decisions`) and `note_attributes(note, key, value_key, value)` (job 157, primary key `(note, key, value_key)`, index on `(key, value_key)`) and `note_text` (job 170: FTS5 `fts5(note UNINDEXED, body, tokenize='trigram')` with FTS5 `secure-delete` on, set once in `Sync` after checking `note_text_config`; one row per plain note with its whole text, none for a note that can't be read or is over `NoteStore.MaxEditBytes`; sensitive notes are never listed, so never indexed; every connection sets `PRAGMA secure_delete = ON`, so a deleted note's text doesn't linger in free pages of `context.db`; the rollback journal `context.db-journal` briefly holds the old pages and is unlinked, not zeroed, after each commit, which is outside this guarantee). `Sync(NoteStore)` updates it
    in one transaction and only re-parses files whose time changed. `PRAGMA user_version` holds the parser
    version (`TagIndex.ParserVersion`, now 10: the heading of a `[[Note#Heading]]` link in `note_links.heading`; 9 was open `- [ ]` lines in `note_todos`; 8 was `Decision:` lines in `note_decisions`; 7 was note text in `note_text`; 6 was a link target that runs into a code span is no link); when it is lower, `Sync` clears every stored time and empties `note_text` in the same
    transaction (so all notes are re-parsed and their text written once) and then sets it. Bump it whenever parsing changes. Queries: `Tags()`, `NotesWithTag`,
    `NoteTags()`, `FindNote`, `Links`, `People()`, `NotesMentioning`, `NotesWithAttribute(key, value)` (by `AttributeParser.ValueKey`), `NotesWithoutAttribute(key)`, `Search(text)` (`(Note, Body)` rows for `note_text MATCH $q`, `$q` the text as one FTS5 phrase: `"` + text with `"` doubled + `"`, always a parameter, so FTS syntax is literal), `Decisions(tag?)` (`(Note, Line, Text, Modified)` rows of `note_decisions` joined to `notes` with a stored time, `Modified` UTC; a tag keeps the notes with that `note_tags` row), `NoteTodos()` (`NoteTodo` rows of `note_todos` joined to `notes` with a stored time). A database that can't be opened or isn't valid throws `TagIndexException` (an
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
    trailing `\r` dropped) containing the query by `OrdinalIgnoreCase` as `SearchHit(Note, LineNumber, Line)` (1-based); a
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
- **Views:**
  - **Review view (job 169):** `ReviewView.Build(notes, todos, calendar, feed, today)` in Core (`ReviewView.cs`) holds
    `From` (today-6) and `To` (today), the calendar's `Zone`, `ChangedNotes` (`ChangedNote`: name and last-write time
    converted to the zone; no sensitive notes; days From..To in the zone; newest first, then name ignoring case),
    `DoneTodos` (done From..To in the zone, `ListDone` order; `DoneDate(todo)`), `Overdue` (open, due < today, `List`
    order), `TodosUnreadable` (both lists empty, the rest still filled), seven `ReviewDay`s today+1..today+7
    (`CalendarStore.Day` with the feed's events; `TimeText` as `CurrentView.Time`), and `Folder`,
    `UnreadableCalendarFiles` (each once), `Status`, `CachedAtUtc`, `Reason` like the current view. The feed is fetched
    once per build; it never opens `context.db`. `context review` prints it; the desktop's `ReviewPanel` (`/review`)
    draws it.
  - **Current view:** all relevant information for now: today's appointments, due todos and linked notes.
    `CurrentView.Build` in Core reads the data (including the untruncated appointments, the time text and the
    calendar's `Zone`, in which a front end shows the cache time); `context current` prints it as text and the
    desktop's `CurrentPanel` draws it. It holds the day (`Today`), the appointments, a `Due` list of every todo with
    `Due <= today` (date, text and `Overdue`; `DueTodo` has no number) and the linked notes. A todo links to notes
    through tags: `TagParser.Parse` on the text of each due todo (at run time, not stored), then
    `TagIndex.NotesWithTag` after `Sync`; each note once, sorted by name ignoring case, with its matching
    tags. Flags instead of exceptions: `TodosUnreadable` for a bad `todos.json` (the day and the appointments are
    still there) and `IndexUnreadable` for a bad `context.db` (the due todos are still there). With no appointment
    and nothing due the index is not synced. Order in both front ends: day, appointments, due todos, linked notes.
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
    `Can't read context.db.` once on stderr (exit 1, after `Can't read todos.json.` when both). `CurrentPanel`
    (`OnOpen`, from `Main.RequestOpen`) starts each appointment row with its grey 1-based number (`td.number`, job 167), draws a `tr.related` row with one `note-button` per note (`Title`, full name
    in `title`) and grey `.matches` under each shown appointment (not counted toward `MaxAppointmentRows`), and red
    `Can't read the related notes.` after the table when the index is unreadable.
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
    `FormatException`. **Recurring events** (job 162): `IcsParser.Parse(text, zone, day)` (used by `Day` and for the
    feed in `CurrentView`) also reads `UID`, `RRULE`, `RDATE`, `EXDATE`, `RECURRENCE-ID` and returns every occurrence
    touching `day` (the `Overlaps` rule); `Parse(text, zone)` (`All`, `PersonView`, the feed check) still gives one
    entry per `VEVENT`, its `DTSTART`. `RecurrenceRule` (`Recurrence.cs`, day numbers only, so no `DateTime`
    overflow) supports `FREQ=DAILY|WEEKLY|MONTHLY|YEARLY`, `INTERVAL`, `COUNT` (DTSTART counts, excluded dates too),
    `UNTIL` (inclusive; date, floating or `…Z`), `BYDAY` (weekly list with `WKST`, monthly/yearly ordinals, daily
    filter), `BYMONTHDAY` (negative too), `BYMONTH`; DTSTART is always the first occurrence. Any other part
    (`BYSETPOS`, `BYWEEKNO`, `BYYEARDAY`, `BYHOUR`/`BYMINUTE`/`BYSECOND`, other `FREQ`), a bad value, `COUNT` with
    `UNTIL` or several `RRULE`s drop only the rule's occurrences. Occurrences keep DTSTART's wall clock in its zone
    and the event's duration, then are converted. `RDATE` adds (no duplicates; `PERIOD` ignored), `EXDATE` removes
    (a date value, or any value on an all-day event, removes that local day); an event with `RECURRENCE-ID` and the
    same `UID` replaces the occurrence starting then (cancelled: only removes); a cancelled series and its overrides
    show nothing. Bounds: without `COUNT` the rule skips ahead to the day; at most `RecurrenceRule.MaxPeriodsPerEvent`
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
    `IKeyStore` or `SealedNote`; `DllImport`/`LibraryImport`/`extern ` appear only in `MacKeyStore.cs`; `X509Store`
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
    `context note show` on an unreadable note answers `Can't read <name>.`
    `context export <note> <path>` writes the note's plain
    text (UTF-8, no BOM, `FileMode.CreateNew`) outside the root (`NoteStore.Contains` resolves links and ignores
    case). `context import <path>` reads a
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
  - **Subpages** (job 152): the children of `X.md` are the `*.md` files in the folder `X/` next to it (exact case
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
    window `dragover`/`drop` at load.
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
    **Todos inside notes** (job 201, `NoteTodoParser.cs`, `NoteTodo.cs`): `NoteTodoParser.Parse(markdown)` gives a
    `ParsedNoteTodo(Line, Text, Due?, Raw)` for each line outside fences (`TagParser.NumberedLinesOutsideFences`) that is
    optional spaces/tabs, `-`/`*`/`+`, one space, `[ ]`, one space, then text; the first whitespace-delimited token
    `due:yyyy-MM-dd` (ASCII digits, a real date, not in a code span) sets `Due` and leaves the text, which is collapsed
    (`TagParser.Collapse`) and cut to `MaxTextLength` (1000, never inside a surrogate pair); an empty text is no todo.
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
    todo by its text and marks it done by its id; the desktop marks the ticked todo done by its id.
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
  current view's sections as text: `Today, <date>`, `Appointments`, `Due`, `Linked notes`; no row caps, `Due` lines as
  `<date>\t<text>`; its error lines go to stderr and make the exit code 1), `review` (job 169, see Review view:
  `Review, <from> to <to>` (`yyyy-MM-dd`), then `Changed notes` (`<yyyy-MM-dd HH:mm>\t<name>`), `Done todos`
  (`<done date>\t<text>`), `Overdue` (`<due>\t<text>`) and `Next week` (`<date>\t<time>\t<title> (<location>)`,
  `(no title)`), every heading always printed; the offline line on stdout like `current`; unreadable calendar files,
  the failed feed and `Can't read todos.json.` on stderr, exit 1, the rest still printed; any argument is a usage
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
  `import <path>`, `export <note> <path>` (paths relative to the current directory, no `~`). When `Migrate(out n)`
  replaced bad todo ids, `todos`, `todo add`, `todo done`, `current` and `review` first write one extra stderr line,
  `Fixed a bad todo id in todos.json.` or `Fixed <n> bad todo ids in todos.json.` (fixed text, the bad value is never
  echoed), and otherwise behave, print and exit as on a valid file; the next run writes no such line. Exit codes: 0 success,
  1 error (one stderr line, nothing on stdout; `current` may write both), 2 usage error (one stderr line ending in
  `Run 'context help'.`; unknown commands never touch the folder). All text from disk goes through `SafeText.Clean`
  (one-line fields also `\n` to space). Tests (`CliTests`) use the
  internal constructor with a fixed zone.
  **`--json`** (job 148; exact token ignoring case, any position, repeats allowed, removed before parsing) makes
  `version`, `todos`, `notes`, `note show`, `tags`, `find`, `search`, `current` and `review` write one compact object + `\n`
  (`CliJson.cs`, `Utf8JsonWriter` with the default encoder, so `+ & ' <` and non-ASCII are `\uXXXX`): `{version}`,
  `{todos:[{due,time,text}]}` (`time` `HH:mm` or `null`), `{notes:[{name,locked}]}`, `{name,text}`, `{tags:[{tag,count}]}` or `{tag,notes:[]}`,
  `{key,value,notes:[]}` (`find`), `{query,results:[{note,line,text}]}` (`search`; `line` an int), `{today,appointments:[{start,end,allDay,
  title,location,related:[{name,tags,words}]}],calendar:{status,cachedAt,unreadableFiles},due:[{due,text,overdue}],linkedNotes:[{name,tags}]}`
  (absent = `null`, tags raw without `#`, times `yyyy-MM-ddTHH:mm:sszzz` in the zone; `due`/`linkedNotes` `null` when
  unreadable, `related` `null` when `context.db` can't be read),
  `{from,to,changedNotes:[{name,modified}],doneTodos:[{due,done,text}],overdue:[{due,text}],appointments:[{start,end,
  allDay,title,location}],calendar:{status,cachedAt,unreadableFiles}}` (`review`; `doneTodos`/`overdue` `null` when
  `todos.json` is unreadable). Names and order are a public contract (`CliJsonTests`, README). Errors stay text; with `help` or a
  write command it is a usage error before anything is read. `README.md` has PowerShell examples.
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
  (Node from `brew install node`) and commit the rebuilt `wwwroot/editor/editor.js` and `editor.css`; `dotnet build`
  and `dotnet test` never need Node. `node_modules` is ignored and `editor/**` is excluded from the C# project.
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
  `needs` all three). Steps of `release`: check out,
  set up .NET, Version, `dotnet test -c Release`,
  `dotnet pack src/Context -c Release -p:Version="$VERSION"`, smoke check, create the GitHub release (notes start
  with the output of `.github/scripts/release-notes.sh <macos>`, written to `$RUNNER_TEMP/release-notes.md`: how to
  open the desktop builds), created with `--draft`.
  - **Draft, then publish:** The release is a draft until `publish` runs: `release` creates it, the desktop jobs upload
    into the draft, and `publish` (no `if:`, so it is skipped when a needed job fails) makes it public. Its steps: check
    out, the version re-check, `Publish GitHub release`, `Prune old prereleases`. The publish step reads the release
    once (`gh release view "$TAG" --json isDraft,targetCommitish,assets`) and fails, publishing nothing, unless it is
    a draft whose `targetCommitish` is the release's commit and that holds `Context.<version>.nupkg`, both macOS zips,
    the Windows `Setup.exe`, this version's full `.nupkg` and `releases.<channel>.json`; only then it runs
    `gh release edit "$TAG" --draft=false` (plus `--prerelease` for a prerelease version). A failed desktop job leaves
    an unpublished draft that neither users nor the updater see (re-run the failed job; `publish` then runs). This is
    what makes immutable releases work: GitHub refuses uploads into a published immutable release, so every asset is
    uploaded first. A draft has no git tag until it is published (develop and release-PR runs); GitHub then creates
    the tag on the draft's commit. Stale drafts of failed runs are not cleaned up, and re-running the whole workflow
    over an existing draft is not supported. The Windows `vpk download github --token` call can see drafts (Velopack
    1.2.161 reads the newest 10 releases and has no draft filter), so a stale draft that holds a higher Windows
    package of the channel becomes the delta base; an installed copy that can't apply a delta falls back to the full
    package. Delete stale drafts by hand.
  - **Versions:** a `develop` push makes a prerelease `X.Y.(Z+1)-develop.<run number>`, one patch above the highest
    release tag (`.github/scripts/develop-version.sh`; `0.0.1-develop.<n>` while there is no tag). A push that changes
    only the root `backlog.md` is skipped (`paths-ignore`) and makes no prerelease. A release PR takes its version from
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
    `Contents/MacOS/`; it neither signs nor zips), then either ad-hoc signed (`codesign -s -`) and zipped with
    `ditto`, or signed with a Developer ID and notarized (see Signing); the asset names are the same either way. (3) The desktop app for Windows, packed by `vpk` (`--packId ContextDesktop`, `--mainExe
    Context.Desktop.exe`): `Setup.exe`, the portable zip, this version's full `.nupkg`, a delta `.nupkg` when
    `vpk download github` found an earlier release of the channel (a failed download only warns), and the feed
    files (`releases.<channel>.json`, `assets.<channel>.json`, `RELEASES*`); never a package of an older version.
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
    variables. `github.token` is the only credential of every other step than the signing step (the create, publish and prune steps, the Windows `vpk download` and the two uploads). The new scripts are POSIX `sh` in the style of `macos-app.sh` (`set -eu`, usage refusal before anything is created, arguments never evaluated, no `set -x`). The three version scripts are Times' (`release-pr-version.sh` differs in one pattern: it refuses every prerelease label, Times' only `-develop`). `ReleaseWorkflowTests.cs` reads `release.yml`
    only through the small YAML reader `tests/Context.Tests/WorkflowYaml.cs` (block mappings and sequences, plain and
    quoted scalars, `|` and `>` blocks, comments; anything else throws), so reformatting the YAML does not break the
    tests, and it runs the scripts with `bash` (also the Windows `Pack` and `Upload` steps and the publish step, against stub `vpk` and `gh` commands, and the macOS signing step and scripts against stub `codesign`, `security`, `xcrun`, `spctl` and `ditto` that log their calls and can fail or answer `Invalid`; never a real keychain, certificate or Apple account). `/factory release --project Context` opens the release PR.
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
    It holds the note files in `NoteStore.cs`, the todo list in `TodoStore.cs`, picking a todo by its text in `TodoPicker.cs`, todos inside notes in `NoteTodoParser.cs` and `NoteTodo.cs`, tag parsing in `TagParser.cs`,
    `[[note link]]` parsing in `NoteLinkParser.cs`, link rewriting on rename in `NoteLinkRewriter.cs`, `@[person]` parsing and the `people` page names in `PersonParser.cs`, person aliases in `PersonAliases.cs`, `key:: value` parsing in `AttributeParser.cs`, the page types in `PageTypes.cs`, the SQLite tag and link index (with `NoteLink`, `NameLookup`) in `TagIndex.cs`, the `.ics` parser and `Appointment` in `IcsParser.cs`, recurrence rules in `Recurrence.cs`, the calendar
    folder reader and `CalendarDay` in `CalendarStore.cs`, the secret link file in `CalendarLink.cs`, the
    download and cache in `CalendarFeed.cs`, the display sanitizer in `SafeText.cs`, bounded file reads (`BoundedFile.cs`, managed code only: a file that reports length 0 is returned empty without being opened, so a FIFO named `x.md` never blocks; an opened stream that can't seek is refused; Core has no P/Invoke, checked by `CoreDependencyTests`) and the view data steps
    `CurrentView.cs` (the current view; meeting prep in `MeetingPrep.cs`), the daily note in `DailyNote.cs`, meeting notes in `MeetingNote.cs`, note templates in `NoteTemplates.cs`, the inbox capture in `Inbox.cs` (its triage queue, remove and snooze in `InboxTriage.cs`), the day's end in `DayEnd.cs` (Times' day file in `TimesDay.cs`, `settings.json` in `SettingsStore.cs`), the morning briefing in `Briefing.cs` (Times' flexitime balance in `TimesBalance.cs`), the todo input rules (`TryParseDate`,
    `MaxTextLength`, `TryParseRepeat`, `NextDue`) in `TodoInput.cs`, the todo repeat in `TodoRepeat.cs`, `TagsView.cs` (tags), `LinksView.cs` (links), `PersonView.cs` (a person page), `FindView.cs` (`find`), `SearchView.cs` (`search`), `TagFilter.cs` (tag filters), `DecisionsView.cs` (`decisions`, with `DecisionParser.cs`), `ReviewView.cs` (the weekly review) and `CalendarLinkView.cs`
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
    only in Debug). `LinkRenamePlan.cs` (job 206) works out, read-only, which plain notes a rename's link rewrite
    changes (for `DesktopState.LinkUpdateCount` and `DesktopActions.RenameNote`). `DesktopState` builds a `DesktopSnapshot` (`Failed`, `Today`, `CurrentView? Current`,
    `Todos`, `Notes`, unreadable flags, `Folder`) off the UI thread with `Refresh()` (one at a time; requests during a run
    coalesce into one more run) and raises `Changed`; `Tick()` refreshes when the day changed or 5 min passed.
    `DataWatcher` (one `FileSystemWatcher` with subfolders, `IsRelevant` filter: the top-level files as before, plus
    a `.md` at any depth except under a folder starting with `.` or under the top-level `calendar/`, and the top-level
    `settings.json` (job 207: saved searches edited outside the app; any case, not in a subfolder); 300 ms
    debounce, 30 s tick timer; never creates the folder) calls them. Components (`App`, `Main`, `CurrentPanel`, `TodoPanel`, `NotesPanel`, `TagsPanel`, `LinksPanel`, `OutlinePanel`, `DayAppointmentsPanel`, `AppointmentPanel`, `PersonPanel`, `ReviewPanel`, `TriagePanel`)
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
    `<parent> can't have subpages. Nothing was done.`), `MoveNote(name, newParent)` (`NoteStore.Move`; the parent is
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
    SHA-256 `Version`, CRLF and BOM flags), mounts the page editor through `EditorInterop` (the only class that touches
    `IJSRuntime`; `[JSInvokable]` `OnTag`/`OnLink`/`OnPerson`/`OnAttachment` only navigate, `OnDrop` only raises `FilesDropped`, `OnChanged` only raises `Changed`) and saves through
    `DesktopActions.SaveNote` -> `NoteStore.SaveIfUnchanged` (unchanged text writes nothing; version re-checked before
    the move; CRLF and BOM kept; temp file mode `0600`). **Auto save** (job 203): `view.js` calls `OnChanged` on every
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
    `/briefing`, `/capture`, `/current`, `/decisions`, `/done`, `/export`, `/find`, `/import`, `/meeting`, `/new`, `/open`, `/review`, `/save`, `/search`, `/settings`, `/tags`, `/today`, `/todo`, `/triage` with `CommandMatcher` (in Core; ties
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
    `/today` (argument ignored) asks `Discard changes to <name>?` first when another note has unsaved changes (not
    when the open note is today's note itself: the editor stays as it is, unsaved text included), then
    calls `DesktopActions.OpenDailyNote()` and opens the note (a sensitive one through the unlock flow), with the
    accent `Created <name>` after a create and the red failure text otherwise.
    `/meeting <number>` (job 167) picks the appointment from the shown `Snapshot.Current` (the `#` column of
    `CurrentPanel`, which counts the rows hidden behind `… N more` too) and works like `/today` through
    `DesktopActions.OpenMeetingNote(appointment)`; a blank or invalid number is red `Type /meeting <number>.`, no
    appointments or a number beyond them the CLI's texts in red, a failed write red `Can't write <name>.`.
    `/briefing` (job 172, hint `Open today's briefing`, argument ignored) works like `/today` through
    `DesktopActions.OpenBriefing()` (`BriefingName()` for the unsaved-changes check): accent `Created <name>` after a
    create, red `briefings is sensitive, so no briefing can be written.` (`BriefingSensitiveText`) or
    `Can't write <name>.` (`BriefingFailedText`).
    **Meeting note:** `MeetingNote` (Core) names the note `yyyy-MM-dd <title>` (local start date, invariant; the
    one-line title with `/ \ : * ? " < > |` and control characters as spaces, collapsed, trimmed, cut to
    `MaxTitlePart` without splitting a surrogate pair, trailing `.md`, dots and spaces stripped, `Meeting` when empty;
    always valid for `ValidateTitle` and equal to `NormalizeTitle`). `Format` writes `# <name>`, `#meeting`, `- Title:`,
    `- Time:` (`MeetingNote.Time`), `- Location:` (only when set) and `- Attendees:` (`@[name]` mentions, or `none`).
    `Ensure(notes, appointment)` returns `Existing` or `Sensitive` without writing, else creates the top-level note
    through `CreateWithText` (a race counts as existing). `Today(calendar, feed, day)` gives the numbered list
    (`CurrentView` order: files plus feed).
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
    `Leaving`, `HasLeft`, `WorkTime` and `Groups` (per work item or name, in first-booked order). Nothing ever writes
    into the Times folder. `DayEnd(notes, todos, calendar, feed, timesFolder, settings?)`: `RefreshWorked(D)` replaces
    or adds D's `## Worked` section (`- Arrived HH:mm, left HH:mm, worked h:mm`, a missing arrival `--:--`, one line
    per group; or the missing/unreadable/absent/not-left line), creating the daily note through `DailyNote.Ensure`;
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
    `Removed saved search <name>.`) are the only writers. End the day is the only item of the More actions menu on today's view (job 200; `Main.RunPageAction("end-day")`
    calls `DesktopActions.EndDay(Snapshot.Today)` and shows the result in `p.main-status`: grey on Done/Unchanged
    (`Day closed. Moved <n> todo(s) to <D+1>.`), else red; `CurrentPanel` has no button and no status line of its own); `Program.cs` calls `DesktopActions.AutoDayEnd()`
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
    today)` writes `# yyyy-MM-dd`, the daily note's `## Appointments` and `## Todos` byte for byte
    (`DailyNote.AppendSections`), `## Open checkpoint` (one `- <date>: #<n> <names>` / `- <date>: <name>` line per
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
    **Pages (job 193):** `Main` holds one `page` (none, triage, tags, settings); opening one replaces another. The note area
    (`div.note-area`: breadcrumb to `LinksPanel`) or `CurrentPanel` gets `hidden` while a page shows, but stays mounted so
    unsaved editor text survives. Each page's root has class `page` and an icon-only `button.close` (aria-label and title
    `Close`) last in `.panel-bar`; `TagPanel` keeps its text `Close`.
    **Settings:** `SettingsPanel` (the `Settings` button in `NotesPanel`, or `/settings`; `Main`'s `page`;
    a page in place of the note area; the × (job 193), opening a note or the current view hides it) has two sections, both
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
    with `TodosUnreadable`): heading `Week review`, `Close`, the grey range, then `Changed notes` (date and a
    `note-button` through `RequestOpen`), `Done todos`, `Overdue` (`overdue` dates), `Next week` (an `h4` per day with
    appointments: time, title, location), the grey empty texts (`No notes changed.`, `No todos done.`,
    `Nothing overdue.`, `No appointments next week.`), red `Can't read todos.json.` in both todo sections and the
    `CurrentPanel` calendar texts. It reloads on `State.Changed` keeping the shown rows; `Close`, opening a note or the
    current view hides it.
    **Tags and links:** `TagsPanel` (the `Tags` button in `NotesPanel`, or `/tags` without an argument; `Main`'s
    `page`) lists `DesktopState.Tags()` (`TagsView.Build`): one button per tag with its grey note count; a click
    shows `TagPanel` for it below the list (`aria-pressed` on that button); its × hides both, `TagPanel`'s only
    itself, and opening a note or the current view hides both. `/tags <tag>` opens `TagPanel` directly. `LinksPanel`
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
    `SearchPanel` (job 170, `/search <text>`, the sidebar's Search button and saved searches through
    `Main.ShowSearch(text)`, which bumps `searchRequest` so the same text starts over; `Main`'s `searchShown`, after
    `FindPanel`) has the heading `Search: <text>` (`Search` when blank) and `Close`, then `input[type=text].search-input`
    (job 207; `aria-label="Search"`, placeholder `Search notes`, maxlength 200, focused on every request; Return searches
    the typed text, Esc closes). A tag filter (`TagFilter.IsTagFilter`) lists `DesktopState.TagFilter(text)` (under
    `indexGate`, never throws) as `note-button`s with no line: grey `No notes match <filter>.`, red `Type a tag filter
    like #project and not #done.` for `Invalid`, red `Search text can be at most 200 characters.` for `TooLong`. Other
    text lists `DesktopState.Search(text)` (`SearchView.Build` under `indexGate`, never
    throws): per hit a `note-button` (through `OnOpen`, full name in `title`) and the grey one-line `span.line` (cut by
    CSS, full text in `title`); at most `MaxRows` (100), then grey `… <n> more` (both kinds). Grey `Loading…`, grey `No notes contain
    <text>.`, red `Can't read the search index.`, grey `Type text or a tag filter like #project and not #done.` for blank (nothing read), red `Type at least
    3 characters to search.` and `Search text can be at most 200 characters.`. When results show (`Found`/`NoMatches`
    of either kind) `button.search-save` `Save search` opens `input.search-save-name` (`aria-label`/placeholder `Search
    name`, maxlength 50, focused): Return calls `Actions.SaveSearch(name, trimmed query)` (its only action), Esc hides
    it; the result is `p.search-save-result` (`role=status`, accent on Done, else red). Reload and stale-result rules as
    the other panels. Opening a note or the current view closes it.
    `DecisionsPanel` (job 180, `/decisions [tag]`; `Main`'s `decisionsShown`, after `SearchPanel`) has the heading
    `Decisions` (with a tag `Decisions: #tag`) and `Close`, and lists `DesktopState.Decisions(tag)` (`DecisionsView.Build`
    under `indexGate`, never throws): per decision a grey `span.date`, a `note-button` (through `OnOpen`, full name in
    `title`), grey `span.people` (only with people) and the one-line `span.text` (cut by CSS, full text in `title`); at
    most `MaxRows` (100), then grey `… <n> more`. Grey `Loading…`, grey `No decisions yet.` / `No decisions in notes
    tagged #<tag>.`, red `Can't read the decisions.`, red `Type /decisions [tag].` for an invalid tag (nothing read);
    same read-only (no `Actions.`), reload and stale-result rules. Opening a note or the current view closes it.
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
    in the main status line. Rules (`DesktopSourceTests`): `WritePlainCopy` and `ReadImport` are named in
    `Context.Desktop` only in `DesktopActions.cs`, and no `.razor` file matches `\b(File|Directory|Path)\.`.
    **Sidebar bar (job 197):** `NotesPanel`'s bar holds only the icon buttons (`.notes-bar-buttons`: search (job 207, magnifying glass, `OnSearch("")`), triage, tags, settings), right-aligned (`justify-content: flex-end`); no logo and no visible "Notes" heading, the top-level `ul.note-list` carries `aria-label="Notes"`. Between the bar and the list, `nav.saved-searches` (job 207, `aria-label="Saved searches"`, only with entries; `DesktopSnapshot.SavedSearches`) has per entry `button.saved-search` (name through `OneLine`, cut, `title` the query; `OnSearch(query)`) and `button.saved-search-remove` (`×`, `aria-label`/`title` `Remove <name>`; `Actions.RemoveSavedSearch`, result in grey/red `p.saved-search-result`); `SavedSearchesUnreadable` shows red `Can't read saved searches.` instead. **Placeholders:** a box's placeholder says only what it is for (`New todo`, `Capture to inbox`), never a `Return to …` hint; Return still submits and there are no submit buttons.
    **Tidy layout (job 192):** the Triage, Tags and Settings buttons in `NotesPanel`'s bar are icon-only (inline `svg`, `aria-hidden`, `currentColor`; `aria-label` and `title` carry the name), note rows show no modified date, and `.top-bar` is a band (`--panel` background, `--line` bottom border).
    **Top bar (job 198):** `TopBar` replaces the capture box as the first child of `Main`'s `main.current-region` (not with
    `Can't read the data folder.`): an empty `.top-bar-start` and right-aligned `.top-bar-buttons` with icon buttons
    `button.capture-open` (`Capture`, job 199: a hand-drawn "T" with an I-beam caret; a `.capture-pop` popover with one
    `input[type=text]` `aria-label="Capture"`, placeholder `Capture to inbox`, maxlength 1000, focused on open, and a
    `.capture-pop-backdrop`), `button.today-open` (`Today`) and `button.day-pick-open` (`Pick a day`; job 213: a `.day-pick` month calendar with a
    `.day-pick-backdrop` and no input: header `button.day-pick-prev`/`day-pick-next` (icon-only, `Previous month`/`Next
    month`, disabled on January 1900/December 2100) around `.day-pick-month` (`MMMM yyyy`, invariant, also the
    `table[role=grid]`'s `aria-label`); columns an empty week header then `Mo`..`Su`; always 6 rows from the Monday on or
    before the 1st, each a `th.week[role=rowheader]` (`ISOWeek` of its Monday, `aria-label` `Week <n>`) and 7
    `td[role=gridcell]` > `button.day` (`aria-label` `dddd, d MMMM yyyy`; `outside` for other months, still clickable;
    disabled outside 1900-01-01..2100-12-31); `TopBar` parameters `Today` (`today`, `aria-current="date"`) and
    `Selected` (`selected`, gridcell `aria-selected="true"`; `Main.SelectedDay`: the open note's day when its name is
    exactly `yyyy-MM-dd`, today with no note and no Triage/Tags/Settings/search/review page, else null). Each opening
    starts on `Selected ?? Today` with that day as the one `tabindex="0"` day (roving tabindex) and focuses it; arrows
    move ±1/±7 days, PageUp/PageDown ±1 month (day clamped), never outside the range, the shown month follows; a click or
    Enter calls `OnPick` once and closes (a later event finds it closed). Date maths in `MonthGrid.cs`; styles only
    theme variables, `.day.today.selected` keeps an inner ring). Esc, the backdrop or the button
    closes a popover; opening one closes the other, so at most one is open. `Main.ShowToday` goes through `RequestOpen(null)` (the unsaved-changes guard) to today's current view.
    `PickDay(DateOnly)` (job 212) works like `/today` for that day: it opens the day's daily note (`yyyy-MM-dd`) in the
    page editor through `Actions.OpenDailyNote(day)` (`DailyNote.Ensure` with `DailyNote.Compose` for that day; the
    no-argument `OpenDailyNote()`/`DailyNoteName()` use the local clock's day), the `running` gate and `ShowDaily`
    (accent `Created <name>`, red failure text, the unlock flow for a sensitive note). The open note of that day stays
    as it is; another dirty note is saved first (`KeptUnsaved`), and when that fails the day waits in
    `pendingPickedDay` behind the `Discard changes to <name>?` dialog. There is no read-only day view: `CurrentPanel`
    only shows today's current view.
    **More actions menu (job 200):** the last top-bar button `button.page-menu-open` (three dots, `aria-label`/`title`
    `More actions`, `aria-haspopup="menu"`, `aria-expanded`) opens `div.page-menu[role=menu]` with a
    `.page-menu-backdrop`; it is one of the three mutually exclusive popovers. `TopBar` gets `[Parameter]
    IReadOnlyList<PageAction> PageActions` (empty: no button), `PageActionsDisabled` (`running` or an open
    Rename/New subpage/Move form) and `EventCallback<string> OnPageAction`. `PageAction(id, label, checked)` is a
    public record (a parameter type must be public); its id is the item's class, `checked` non-null makes a
    `menuitemcheckbox` with `aria-checked`. `Main.PageActionsFor(snapshot)` builds the list: none while a page
    (Triage, Tags, Settings) is shown or another day is picked; `end-day` on today's view; for a plain note `rename`,
    `delete`, `history`, `new-subpage`, `move`, `open-external`, `export`, `mark-sensitive` (when supported); for a
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
    `Note (N)`, `Todo (T)`, `Add to note (A)`, `Drop (D)`, `Later (L)`; the keys n/t/a/d/l (any case, outside a text
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
    `TodoPanel` has an entry row (`DesktopActions.AddTodo`). `NotesPanel` has a new-note entry row (text field + `New
    note`; Enter or the button calls `CreateNote`; on Done the field clears, the note opens through `Main`'s `RequestOpen`
    (a dirty editor asks first) and a `role="status"` line shows `Created <name>` (accent), else the red text). While a
    note is open its actions are in the top bar's More actions menu (also when the note can't be read; job 200), and
    `.note-toolbar` above it exists only while a form is open: `Rename` swaps in a prefilled name
    field (Enter or `Save name` renames and reopens the note under the new name; Esc or `Cancel` closes the form;
    a dirty editor is refused with red `Save <name> first.`), `Delete` asks `Delete <name>? This can't be undone.`
    (`ConfirmDialog`, confirm label `Delete`; on confirm the main region returns to the current view) and `Open in
    default editor` calls `OpenExternally` (red text on failure). Subpages (job 152): `NotesPanel` draws the notes
    as nested `ul.note-list` lists (`note-children` below the top), each row showing `Title` with the full name in
    `title`, and a toggle `<button>` (`aria-expanded`, `aria-label` `Expand <name>`/`Collapse <name>`) for a note with
    subpages; notes start collapsed, `Main` passes `OpenName` so opening a note expands its ancestors, and the
    expanded set survives `State.Changed`. An open subpage shows `<nav class="breadcrumb" aria-label="Breadcrumb">`
    as the first child of `.note-area`: each ancestor a `button.crumb` opening it through `RequestOpen`, the last item a span with
    `aria-current="page"`. The plain-note menu also has `New subpage` (field `Subpage title`, `Create subpage` /
    `Cancel`, Enter/Esc; Done opens the page and shows `Created <full name>`) and `Move` (field `New parent`, prefilled
    with the parent, empty = top level, `Move here` / `Cancel`; Done reopens the note and shows `Moved to <name>`);
    both, like Rename, refuse a dirty editor with `Save <name> first.`. Rename prefills `Title`, Export suggests
    `<Title>.md`, and Delete of a note with subpages asks `Delete <name> and its <n> subpages? This can't be undone.`
    (`1 subpage` for one). The palette's `/new <title>` does what the entry row
    does (`/new` alone: red `Type /new <title>.`). The page editor is `wwwroot/editor/editor.js`
    (`globalThis.ContextEditor`), built from `editor/src/` (`schema.js`, `markdown.js` with markdown-it `html: false`,
    `view.js`, `index.js`); the JS `isLossless` stays for the bundle tests, but the desktop never calls it (job 210: no
    reformat line, no `.lossy`, no `EditorInterop.IsLosslessAsync`).
    Job 202: `NoteEditor` passes `Name` to `mount` (5th argument); `splitTitle(markdown, name)` keeps a
    first line `# <name>` (ignoring case and surrounding spaces, or the name's last segment) and the blank lines after
    it as a hidden head that is never parsed and that `getMarkdown`/`roundTrip` put back byte for byte, and inside the
    editor a level-1 heading keeps `level` 1 (saved as `# `) but is drawn as `h2` (`headingDom`), while typing `# ` makes level 2 (`typedBlock` tests the input rules without a view).
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
    at the top and grey `<name> has no heading <heading>.` (both through `DesktopText.OneLine`); a missing note is still
    `No note named <target>.`. `OutlinePanel` (`nav.outline-panel`, `aria-label="Outline"`) sits in `Main`'s right sidebar
    after `DayAppointmentsPanel` in `aside.todos-region`, before `TodoPanel` (job 210; `.todos-region .outline-panel`: own 40vh
    scroll, `--line` border), and shows one `button type="button"` (`outline-item level-<n>`, plain text) per heading from
    `OutlinePanel.MinHeadings` (3) on; a click calls `scrollToHeading` with its index and never takes focus by itself.
    **Sidebar appointments** (job 214): `DayAppointmentsPanel` (`section.day-appointments`, `aria-label="Appointments"`)
    is the first child of `aside.todos-region` in every state. Its `h2.day-appointments-day` reads `Today` for
    `Snapshot.Today`, else `dddd, d MMMM yyyy` (invariant); icon-only `button.day-prev`/`day-next` (`Previous day`/`Next day`)
    step the day, disabled at 1900-01-01/2100-12-31 (the chosen day is in memory only; back on today it follows the
    snapshot). It lists `DesktopState.DayAppointments(day)` (calendar files plus the feed's cached copy through
    `ReadCache` + `CurrentView.FeedEvents`, never a download; `Task.Run`, never throws, with the state's UTC clock in
    `Zone` as `LocalNow`) through `AppointmentList.Shown` (Core, pure: on today the last past appointment, `End <= now`,
    last in `CalendarStore.Day` order, plus every one not yet past; other days all) as `button.appointment-item`
    (`CurrentView.Time` for that day, `OneLine` title or `(no title)`); grey `No appointments.`, red
    `Can't read calendar/<file>`; it reloads on `State.Changed` keeping the rows, a day change shows `Loading…`. A click
    goes to `Main.OpenAppointment` (save first, else `Discard changes to <name>?`; Discard drops the note and shows the page
    over the current view): `Page.Appointment` shows `AppointmentPanel` (`section.appointment-page`, `h2` title, `Close`;
    the outline is hidden, opening any note closes it). Same-titled appointments (ordinal) on the day share one page (a
    second click keeps it, no reload). It lists `DesktopState.AppointmentPage(title, day)` (`MeetingPrep.Related(...,
    max: null)` over the title and the union of the attendees of those appointments; `Sync`, `NoteTags()`, `List()`
    under `indexGate`; any failure gives `IndexUnreadable`, red `Can't read the related notes.`) as `note-button`s
    (title, full name in `title`, grey `.matches`) through `RequestOpen`; with none, `button.create-note-link`
    `Create a new note` runs the `/meeting` steps (`Main.CreateAppointmentNote`, the first such appointment re-read
    through `DayAppointments`) and opens the meeting note with accent `Created <name>`.
    `NoteEditor` renders none: it reloads the list after the mount, after every `Done` save (auto or explicit),
    `ReloadAsync` and `ShowSavedAsync` and reports each through `OnOutline` (empty when disposed or locked); `Main` keeps
    the current editor's latest list (each editor's callback carries the generation it was rendered with, so a report
    from an older editor is ignored; its receiver is no component, so only a changed list renders `Main`; opening
    another note, closing or locking clears it), hides it while a page or the
    search panel is shown, and `OnJump` calls `NoteEditor.JumpToIndexAsync`. An editor that didn't start, an unreadable
    note or failing `headings` shows none, and a locked sensitive note's outline goes with its editor (its headings are only ever read from the unlocked editor, never indexed).
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
    `wwwroot/index.html` (CSP meta tag, `img-src 'self' blob:` for attachment images, only `_framework/blazor.webview.js` and `editor/editor.js`), `wwwroot/app.css`
    and `wwwroot/editor/editor.css`.
  - **Desktop rules:** the Core writing calls (`Create`, `Save`, `SaveIfUnchanged`, `Add`, `Complete` (also `NoteTodos.Complete`), `Set`, `Clear`, `Delete`,
    `Rename`, `CreateChild`, `Move`, `MoveWithNote`, `RemoveUnlinked` (`NoteAttachments`), `ClearCache`, `Capture` (`Inbox.Capture`), `MarkNotified` (named only in `TodoStore.cs` and `DesktopActions.cs`), `Snooze` (`InboxTriage.Snooze`; `InboxTriage.Remove` is checked on its own, since components remove from their own lists), `Ensure` (`DailyNote.Ensure`, `MeetingNote.Ensure`, `Briefing.Ensure`), the note history (`Keep`, `RemoveHistory`, `RestoreVersion`; only the call `Actions.RestoreVersion(` in a `.razor` file is allowed), `CreateWithText` (`CreateNoteFromTemplate`), the day's end (`CloseDay`, `RefreshWorked`, `MoveTodos`, `AutoClose`, `MoveDue`, `SaveDayEnd`), and for sensitive notes `Seal`, `AddRecipient`, `DeleteSensitive`, `CreateMachineKey`,
    `StoreRecoveryPublicKey`) appear only in `DesktopActions.cs` (exemptions: `DotNetObjectReference.Create(` once in
    `EditorInterop.cs`, `ECDiffieHellman.Create(` in `SealedNote.cs`, and `WindowsKeyStore.cs`, whose .NET calls change
    the certificate store and which names no Core store, `File.`, `Directory.` or `Path.`; the reads still write
    only through `TagIndex.Sync` and `CalendarFeed.Fetch`, as on the command line). `DesktopActions` is named only by
    `.razor` files, `Program.cs` and `StateComponent`, never by `DesktopState`, `DataWatcher` or `DesktopSnapshot`, so
    nothing writes on render, refresh, the watcher or the tick. The named exception (jobs 171 and 172): `Program.cs`'s
    `AutoWrites` (once at start and in the day-end timer) calls `DesktopActions.AutoDayEnd()` and then
    `DesktopActions.AutoBriefing()`, and (job 178) `DesktopActions.AutoReminders()` runs once at start before
    `AutoWrites` and on its own timer every `ReminderInterval` (30 s, first after half of it, so it doesn't collide
    with the day-end timer), the only methods `Program.cs` calls on the actions and named by no other `.cs` or
    `.razor` file, so both automatic writes (the day's end and the morning briefing) still write only through
    `DesktopActions.cs`; `Briefing.Ensure` and `DesktopState.OfferBriefing` are called only there. In `.razor` files every `@code` method that calls
    `Actions.` is bound by an event attribute in the same file (`@on<event>=` on an element or an `On<Event>=`
    callback parameter on a component), is no lifecycle method (`OnInitialized*`, `OnParametersSet*`,
    `OnAfterRender*`, `SetParametersAsync`, `BuildRenderTree`) and is called by no other method; no `.cs` file calls
    `Actions.`. Destructive actions take a `Confirmation`, and `new Confirmation(` appears only in
    `ConfirmDialog.razor`.
    `Velopack` is named only in `VelopackUpdateSource.cs` and `Program.cs`; the update classes (`IUpdateSource.cs`,
    `StartupUpdate.cs`, `VelopackUpdateSource.cs`) never mention `NoteStore`, `DefaultFolder`, `DesktopActions`,
    `Environment.GetEnvironmentVariable`, `args` or `Console.`, so an update never touches the data folder; no test
    references `VelopackUpdateSource` (tests use `FakeUpdateSource`, never the network).
    **Launcher rule:** `Process` / `System.Diagnostics` appear only in `ShellNoteLauncher.cs` and (job 178)
    `SystemNotifier.cs`, each named only in itself and in `Program.cs`; no test names `SystemNotifier` (tests use
    `FakeNotifier`, which records `(title, body)` and can throw), and the notifier passes the text only as arguments or
    environment values. The only path `ShellNoteLauncher` ever gets is a checked note or attachment path (a note from `List()`, or an
    attachment PDF `NoteAttachments.TryResolve` accepted; no user text becomes a path or an argument); no test references `ShellNoteLauncher` (tests use `FakeNoteLauncher`, which records paths and can throw).
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
    names either again) and `FakeHttpHandler.cs` (canned HTTP responses). The view tests (`CurrentViewTests`, `ReviewViewTests`, `TagsViewTests`, `LinksViewTests`, `FindViewTests`, `SearchViewTests`, `DecisionsViewTests`, `CalendarLinkViewTests`) check the view data field by field. Note, todo, tag and calendar tests use their
    own temp folder (deleted in `Dispose`), never the real app data folder.
  - `tests/Context.Desktop.Tests/`: xUnit + bunit, references `Context.Desktop` and `Context.Core`; one test class per
    component or class, plus `EditorBundleTests` (Jint on the committed `editor.js` against `RoundTrip/lossless` and
    `RoundTrip/lossy` fixtures, compared with the C# parsers; `.gitattributes` keeps their bytes), `EditorJs` (bunit
    JSInterop setups for the page editor), `EditingFlowTests` and `TodoEntryTests`, `StartupUpdateTests` (with `FakeUpdateSource`), `NoteActionsTests` (note actions, with `FakeNoteLauncher`), `CaptureActionsTests` (`CaptureText`), `TriageActionsTests` (the triage actions) and `TriagePanelTests` (the panel, `/triage` and the `Triage` button through `Main`), `NoteTransferActionsTests` (`ImportNote`, `ExportNote`; sources and targets in `DesktopFixture.Outside`, with `FakeFileDialog` for the panels), `SettingsPanelTests` (with the link-secrecy marker test through `Main`), `ReviewPanelTests`, `CalendarLinkActionsTests`, `DayEndActionsTests` (`EndDay`, `AutoDayEnd`; Times files in `DesktopFixture.TimesFolder`), `BriefingActionsTests` (`AutoBriefing`, `OpenBriefing`), `ReminderActionsTests` (`AutoReminders`, with `FakeNotifier`) and `TimedTodoTests` (the palette's timed `/todo`), `KeyDetailsTests`, `DesktopStrongNameTests` (the `Context.snk` key on `Context.Desktop` and its tests, keyed `InternalsVisibleTo`), `DesktopDependencyTests` (no terminal UI library, no Jint
    outside tests, exact npm versions, Velopack only in `Context.Desktop`), `DesktopSourceTests` (raw markup, risky elements,
    CSP, write sites, event-handler-only actions, button types, checkbox-only inputs, JS interop, the launcher rule, the calendar-link, browser-storage and logging rules, the transfer calls and no file system class in `.razor`), `DesktopFixture` (temp folder, deleted under the state's `indexGate` so a panel's index read never races it; injected
    day/clocks, `FakeHttpHandler` linked from `Context.Tests`, a `BunitContext` serving the state and the actions),
    `FileSnapshot` (bytes and last-write times of a folder, to prove nothing changed), `RepoFiles` and `WaitTimeoutSourceTests` (the wait rule). A `WaitForAssertion` that waits for work behind `Task.Run` (such as
    every wait in `SensitiveFlowTests`, which runs real ECDH and AES-GCM) passes an explicit, bounded timeout from a named
    constant in its class, and no test ever changes `BunitContext.DefaultWaitTimeout`. The guard finds every `.cs` file under `tests/Context.Desktop.Tests/` that calls `WaitForAssertion`, `WaitForState` or `WaitForElement`; each has exactly one `Wait` constant (10-30 s) passed as the last argument of every wait; a new test file needs no registration. Any component's render re-parses the whole rendered markup, so a test
    that compares elements or clicks right after opening a note first waits until the page's own `Task.Run` loads
    have rendered (job 175: `MainTests.OpenFull` waits until `.links-panel` shows no `Loading…`). A `WaitForAssertion`/`WaitForState` re-checks only after a render, so a wait on state that is not rendered (set off the renderer with no render after it) awaits a task with the class's `Wait` instead (job 204: the window close completes `DesktopFixture.Closed`, and no wait lambda reads `CloseCalls`, checked by `WaitTimeoutSourceTests`; `DesktopFixture.CloseAfter` holds a close until a test lets it go).
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
