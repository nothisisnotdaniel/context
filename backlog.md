# Context backlog

`/factory next --project Context` runs the first open item, checks it off and records the job id.
Each item is a user story in its own file under `backlog/`, numbered `NNN-slug.md`; the line links it as `#N`.
The order of the lines is the priority. Add items with `python3 factory/tools/job.py add --project Context '<json>'`
(or by hand: write the story file first, then the line), and keep a number once it is given.
`- [?]` items are follow-ups the factory proposed: change `[?]` to `[ ]` to approve one, or delete the line and its file.
`- [h]` items are tasks for you. `(after: #3)` waits until item 3 is done; `(after: Times#5)` names another project's item.

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
- [x] Calendar: pull in today's appointments and show them in /current → 028-calendar
- [x] Exchange calendar via published link: let the user configure the ICS link that Outlook/Exchange gives when a calendar is published (Settings → Calendar → Shared calendars → Publish). `/current` downloads it (HTTPS only, with a timeout and the same 5 MB cap) and shows its appointments through the existing `IcsParser`, next to the files in `calendar/`. Keep a cached copy so `/current` still works offline, and treat the link as a secret (never show it in full or log it). Microsoft Graph sign-in is out of scope. → 031-exchange-ics-link
- [x] Core library: move `NoteStore`, `TodoStore`, `TagParser`, `TagIndex`, `IcsParser`, `CalendarStore`, `CalendarLink`, `CalendarFeed` and `SafeText` into a new `src/Context.Core` class library (no Spectre reference, `InternalsVisibleTo` for tests and front ends); the console app references it and behaves exactly as before. No new packages. → 032-core-library
- [x] Views without Spectre: split each `*Commands` class into a data step in Core (e.g. `CurrentView.Build(...)` returns appointments, due todos, linked notes and warnings) and a Spectre renderer in the console app, so `/current`, `/notes`, `/tags`, `/links` and `/calendar-link` can be shown by any front end; console output stays byte-for-byte the same. → 034-views-without-spectre
- [x] Optional encryption for everything in Context: let the user turn on encryption at rest for all data Context stores — notes (`*.md`), `todos.json`, `context.db`, the calendar cache and `calendar-link.txt`. Off by default. Key from a passphrase (strong KDF) or the OS key store (Windows DPAPI / macOS Keychain); spec to decide and keep notes editable (e.g. an export/decrypt command). Never write decrypted data to disk; existing plain data is migrated only on the user's command, with no data loss on a wrong passphrase. → 035-optional-encryption
- [x] Core has no Spectre test: add a test that fails if `Context.Core` references Spectre.Console (assembly references or package references). → 039-core-no-spectre-test
- [x] No raw ToString on views: `CurrentView`, `TagsView` and `LinksView` are records whose built-in `ToString()` prints raw, uncleaned text; override it (e.g. to the type name) or otherwise make sure no front end logs or shows it. → 042-views-tostring
- [x] Document view fields: doc comments on `CurrentView.Status` (the Stale line needs a non-null `CachedAtUtc`) and on the `TagsView`/`LinksView` fields that only some kinds use, so other front ends render them right. → 044-document-view-fields
- [x] Settle `CurrentView.Zone`: the property was added beyond the 034 spec so the renderer can show the stale cache time; keep and document it, or let the renderer get the time zone another way. → 046-settle-view-zone
- [x] Note links: `[[Note name]]` in a note links to the note with that name (matched like `/open`: ignoring case, optional `.md`). Store these links in the database next to the tag links, and show them in `/links <note>` (both notes it links to and notes that link to it). A link to a note that doesn't exist yet is kept and shown as missing. Keep `#[tag]` for tags with spaces (job 024). → 048-note-links
- [x] Desktop app (read-only): add `src/Context.Desktop` for macOS and Windows using Blazor in a native web view (Photino.Blazor: WebView2 on Windows, WKWebView on macOS), with a window showing `/current`, the todo sidebar and the notes list from the same data folder, refreshed when files change. All page text uses Blazor's default encoding (no `MarkupString` for outside text), a strict content security policy, and the calendar link never reaches the page. Component tests with bunit. New packages (Photino.Blazor, bunit) need a reason in the spec. → 053-desktop-app-readonly
- [x] Desktop writes: lift the desktop app's read-only rule. All writes go through the same `Context.Core` stores and checks as the console (names, sizes, encryption), from Blazor event handlers only; destructive actions (delete, overwrite, turning encryption off) ask for confirmation in the window; the CSP and the no-`MarkupString` rule stay. Update the desktop rules in CLAUDE.md and `DesktopSourceTests` to match. Full path, high risk. First step of the desktop move: the desktop app becomes the only UI and the console becomes a scriptable CLI. → 080-desktop-writes
- [x] Desktop editing: WYSIWYG Markdown editing of notes in the desktop app with a bundled JavaScript editor (no CDN; e.g. Milkdown or TipTap, spec to choose). `#tag`, `#[tag with spaces]` and `[[Note links]]` are editor nodes so they are never escaped or reformatted; raw HTML in notes is off. Load and save go through `NoteStore` (save checks the file wasn't changed since it was opened), and a round-trip test opens and saves notes and checks the `.md` files are unchanged. Also: add and complete todos with a date picker, clickable tags and links, and a Ctrl+K command palette that uses `CommandRegistry.Match`. → 094-desktop-editing
- [x] Scriptable CLI: turn the console app into a non-interactive tool for automation from PowerShell or any shell. `context <command> [args]` runs one command and exits (0 on success; non-zero with a message on stderr on an error), with no layout, popup, line editor or prompts. Start with `capture`, `todo add`, `todo done`, `todos`, `notes`, `note show`, `tags`, `links`, `current`, `import`, `export` and `help`. Plain text output by default. With encryption on, the passphrase comes from the `CONTEXT_PASSPHRASE` environment variable or from stdin with `--passphrase-stdin`, never from an argument. Safe to run while the desktop app is open (SQLite busy timeout, atomic file writes). Full path, high risk. → 123-scriptable-cli
- [x] Desktop todos: add a todo with a due date (date picker plus today and tomorrow shortcuts) and mark todos done in the desktop app. Builds on desktop writes. → done in 094-desktop-editing (entry row with date picker, Today/Tomorrow, checkbox done)
- [x] [#1](backlog/001-github-actions-ci.md) GitHub Actions CI → 129-github-actions-ci
- [x] [#16](backlog/016-release-workflow.md) Release workflow → 131-release-workflow
- [x] [#2](backlog/002-flaky-feed-tests.md) Flaky feed tests → 130-flaky-feed-tests
- [x] [#3](backlog/003-remove-folder-encryption.md) Remove folder encryption → 132-remove-folder-encryption
- [x] [#116](backlog/116-unsigned-desktop-builds-in-the-release.md) Unsigned desktop builds and Windows auto-update → 133-desktop-builds-auto-update
- [x] [#4](backlog/004-desktop-notes.md) Desktop notes → 134-desktop-notes
- [x] [#123](backlog/123-publish-releases-only-after-all-desktop.md) Publish releases only after all desktop assets are uploaded → 135-release-draft-publish
- [ ] [#5](backlog/005-sensitive-notes-with-certificate-unlock.md) Sensitive notes with certificate unlock (after: #3; #4)
- [ ] [#6](backlog/006-desktop-tags-and-links.md) Desktop tags and links
- [ ] [#7](backlog/007-desktop-settings.md) Desktop settings
- [ ] [#8](backlog/008-desktop-import-and-export.md) Desktop import and export
- [ ] [#9](backlog/009-quick-capture-in-the-desktop-app.md) Quick capture in the desktop app
- [ ] [#10](backlog/010-editor-dirty-marker.md) Editor dirty marker
- [ ] [#11](backlog/011-keyboard-focus-for-chips.md) Keyboard focus for chips
- [ ] [#12](backlog/012-clear-editor-mount-error.md) Clear editor mount error
- [ ] [#13](backlog/013-console-todos-without-numbers.md) Console todos without numbers
- [ ] [#14](backlog/014-remove-the-interactive-console.md) Remove the interactive console (after: #4; #7; #8; #6)
- [ ] [#15](backlog/015-signed-desktop-builds.md) Signed desktop builds (after: #116)
- [ ] [#17](backlog/017-json-output-for-the-cli.md) JSON output for the CLI
- [ ] [#18](backlog/018-write-todo-ids-once.md) Write todo ids once
- [ ] [#19](backlog/019-survive-a-bad-todo-id.md) Survive a bad todo id
- [ ] [#20](backlog/020-complete-checks-the-whole-todo.md) Complete checks the whole todo
- [ ] [#21](backlog/021-test-for-a-todo-removed-elsewhere.md) Test for a todo removed elsewhere
- [ ] [#22](backlog/022-concurrent-todo-writes.md) Concurrent todo writes
- [ ] [#23](backlog/023-voice-capture-spike.md) Voice capture spike
- [ ] [#24](backlog/024-voice-notes.md) Voice notes (after: #23)
- [ ] [#25](backlog/025-outlook-as-a-source.md) Outlook as a source
- [ ] [#26](backlog/026-recurring-appointments.md) Recurring appointments
- [ ] [#27](backlog/027-todo-dates-with-years.md) Todo dates with years
- [ ] [#28](backlog/028-tag-and-link-parser-fixes.md) Tag and link parser fixes
- [ ] [#29](backlog/029-index-limits.md) Index limits
- [ ] [#30](backlog/030-tag-help-and-input.md) Tag help and input
- [ ] [#31](backlog/031-limit-current-rows.md) Limit /current rows
- [ ] [#32](backlog/032-regular-files-only.md) Regular files only
- [ ] [#33](backlog/033-sturdier-calendar-parsing.md) Sturdier calendar parsing
- [ ] [#34](backlog/034-clearer-calendar-display.md) Clearer calendar display
- [ ] [#35](backlog/035-strong-name-core.md) Strong-name Core
- [ ] [#36](backlog/036-claude-md-tidy-ups.md) CLAUDE.md tidy-ups
- [ ] [#37](backlog/037-note-store-cleanup.md) Note store cleanup
- [ ] [#38](backlog/038-core-dependency-test-tidy-ups.md) Core dependency test tidy-ups
- [ ] [#39](backlog/039-safe-tostring-on-all-records.md) Safe ToString on all records
- [ ] [#40](backlog/040-view-doc-tidy-ups.md) View doc tidy-ups
- [ ] [#41](backlog/041-zone-in-calendarlinkview.md) Zone in CalendarLinkView
- [ ] [#42](backlog/042-label-shared-tag-rows.md) Label shared-tag rows
- [ ] [#43](backlog/043-daily-note.md) Daily note
- [ ] [#44](backlog/044-note-templates.md) Note templates
- [ ] [#45](backlog/045-meeting-notes-from-appointments.md) Meeting notes from appointments
- [ ] [#46](backlog/046-meeting-prep.md) Meeting prep
- [ ] [#47](backlog/047-full-text-search.md) Full-text search
- [ ] [#48](backlog/048-recurring-todos.md) Recurring todos
- [ ] [#49](backlog/049-weekly-review.md) Weekly review
- [ ] [#50](backlog/050-times-bookings-in-current.md) Times bookings in /current
- [ ] [#51](backlog/051-people-pages.md) People pages
- [ ] [#52](backlog/052-recent-and-pinned-notes.md) Recent and pinned notes
- [ ] [#53](backlog/053-note-history.md) Note history
- [ ] [#54](backlog/054-attachments.md) Attachments
- [ ] [#55](backlog/055-timed-reminders.md) Timed reminders
- [ ] [#56](backlog/056-one-project-list.md) One project list
- [ ] [#57](backlog/057-inbox-triage.md) Inbox triage
- [ ] [#58](backlog/058-flagged-mail-to-the-inbox.md) Flagged mail to the inbox
- [ ] [#59](backlog/059-ask-context.md) Ask Context
- [ ] [#60](backlog/060-decision-log.md) Decision log
- [ ] [#61](backlog/061-resurfacing.md) Resurfacing
- [ ] [#62](backlog/062-links-to-headings.md) Links to headings
- [ ] [#63](backlog/063-project-page-per-work-item.md) Project page per work item
- [ ] [#64](backlog/064-search-across-context-and-times.md) Search across Context and Times
- [ ] [#65](backlog/065-todos-inside-notes.md) Todos inside notes
- [ ] [#66](backlog/066-rename-with-link-update.md) Rename with link update
- [ ] [#67](backlog/067-email-to-note.md) Email to note
- [ ] [#68](backlog/068-saved-searches.md) Saved searches
- [ ] [#69](backlog/069-board-view-for-a-tag.md) Board view for a tag
- [ ] [#70](backlog/070-day-s-end-in-the-daily.md) Day's end in the daily note
- [ ] [#71](backlog/071-morning-briefing.md) Morning briefing
- [ ] [#72](backlog/072-command-bar.md) Command bar (after: Times#4)
- [ ] [#73](backlog/073-mail-digest.md) Mail digest
- [ ] [#74](backlog/074-waiting-for-list.md) Waiting-for list
- [ ] [#75](backlog/075-todos-tied-to-meetings.md) Todos tied to meetings
- [ ] [#76](backlog/076-done-log.md) Done log
- [ ] [#77](backlog/077-quick-switcher.md) Quick switcher
- [ ] [#78](backlog/078-smart-paste.md) Smart paste
- [ ] [#79](backlog/079-share-a-note.md) Share a note
- [ ] [#80](backlog/080-agenda-carry-over.md) Agenda carry-over
- [ ] [#81](backlog/081-topics-for-the-next-1-1.md) Topics for the next 1:1
- [ ] [#82](backlog/082-review-dates.md) Review dates
- [ ] [#83](backlog/083-glossary.md) Glossary
- [ ] [#84](backlog/084-screenshot-to-inbox.md) Screenshot to inbox
- [ ] [#85](backlog/085-similar-notes.md) Similar notes
- [ ] [#86](backlog/086-mermaid-diagrams.md) Mermaid diagrams
- [ ] [#87](backlog/087-standup-notes.md) Standup notes
- [ ] [#88](backlog/088-vacation-handover.md) Vacation handover
- [ ] [#89](backlog/089-call-to-meeting-note.md) Call to meeting note
- [ ] [#90](backlog/090-keep-mixed-line-endings.md) Keep mixed line endings
- [ ] [#91](backlog/091-windows-device-check-at-open.md) Windows device check at open
- [?] [#92](backlog/092-times-inside-the-context-desktop-app.md) Times inside the Context desktop app
- [?] [#93](backlog/093-phone-companion-app.md) Phone companion app
- [?] [#94](backlog/094-browser-extension.md) Browser extension
- [?] [#95](backlog/095-local-ai-for-ask-context.md) Local AI for Ask Context
- [?] [#96](backlog/096-excalidraw-drawings.md) Excalidraw drawings
- [?] [#97](backlog/097-azure-devops-status.md) Azure DevOps status
- [?] [#98](backlog/098-sharing-notes-with-key-exchange.md) Sharing notes with key exchange
- [?] [#99](backlog/099-refuse-odd-files-on-import.md) Refuse odd files on import
- [?] [#100](backlog/100-revisit-ac-8-texts-with-json.md) Revisit AC-8 texts with --json
- [ ] [#101](backlog/101-work-item-references.md) Work item references
- [ ] [#102](backlog/102-work-item-dossier.md) Work item dossier (after: #101; #17; Times#37; #108)
- [ ] [#103](backlog/103-meeting-flow.md) Meeting flow (after: Times#4; #108)
- [ ] [#104](backlog/104-decision-log.md) Decision log (after: #101)
- [ ] [#105](backlog/105-handover-pack.md) Handover pack (after: #102; #104; #108)
- [ ] [#106](backlog/106-claude-code-mcp-server.md) Claude Code MCP server (after: #101; #17; Times#37; #108)
- [ ] [#107](backlog/107-own-cloud-sync-spike.md) Own-cloud sync spike
- [ ] [#108](backlog/108-pro-licence-in-context.md) Pro licence in Context (after: Times#39)
- [ ] [#109](backlog/109-install-guide-for-the-unsigned-desktop.md) Install guide for the unsigned desktop app
- [ ] [#110](backlog/110-narrow-the-node-check-in-the.md) Narrow the node check in the CI workflow test
- [ ] [#111](backlog/111-concurrency-and-timeout-for-the-release.md) Concurrency and timeout for the release workflow
- [ ] [#112](backlog/112-locked-package-restore-for-release-builds.md) Locked package restore for release builds
- [ ] [#113](backlog/113-refuse-removed-secret-options-after-a.md) Refuse removed secret options after a command
- [ ] [#114](backlog/114-owner-only-file-mode-for-all.md) Owner-only file mode for all data files
- [ ] [#115](backlog/115-bounded-reads-for-todos-calendar-link.md) Bounded reads for todos, calendar link and notes
- [x] [#117](backlog/117-make-the-github-repo-public.md) Make the GitHub repo public
- [ ] [#118](backlog/118-refuse-a-newline-in-version-in.md) Refuse a newline in VERSION in the release scripts
- [ ] [#119](backlog/119-fetch-the-earlier-desktop-package-with.md) Fetch the earlier desktop package with gh release download
- [ ] [#120](backlog/120-pin-the-nuget-source-for-the.md) Pin the NuGet source for the desktop packages
- [ ] [#121](backlog/121-open-empty-notes-in-the-default.md) Open empty notes in the default editor on macOS and Linux
- [ ] [#122](backlog/122-update-links-when-a-note-is.md) Update [[links]] when a note is renamed in the desktop app
- [?] [#124](backlog/124-check-the-tag-and-uploaded-assets.md) Check the tag and uploaded assets before publishing a release
- [?] [#125](backlog/125-handle-a-leftover-draft-with-the.md) Handle a leftover draft with the same tag
- [?] [#126](backlog/126-keep-stale-drafts-out-of-the.md) Keep stale drafts out of the Windows delta base

## Dropped with the terminal UI

These were about the interactive console, which the desktop move removes. Kept for reference; not picked up by the factory.

- Helpful empty todo sidebar: "No todos yet" doesn't say how to add one. Mention `/todo` in the empty sidebar, e.g. "No todos yet — add one with /todo".
- Spacing in /current: add a blank line between the "Today, <date>" title, the Due table, and the Linked notes table (or its "No linked notes" / "Can't read context.db" line).
- Better calendar-link failure line: add "see /calendar-link" to the red "Can't download the Exchange calendar" line, and show a "Downloading Exchange calendar…" hint while `/current` waits (up to 10 s).
- Shorter /calendar-link popup text: shorten the command description so it fits in a narrow terminal's popup.
- Masked link input: hide the link while it is typed after `/calendar-link` (e.g. show `•` per character), so it never lands on screen or in scrollback. Until then, mention the echo in `CLAUDE.md`.
- Encryption UX polish: `/encrypt` re-asks after a passphrase shorter than 12 characters, the `/decrypt` success message mentions that the tag index is rebuilt, and the encryption warnings and hints fit narrow terminals.

## Dropped with folder encryption

Whole-folder encryption is removed (see "Remove folder encryption"); only sensitive notes are encrypted. Kept for reference; not picked up by the factory.

- OS key store unlock: unlock encryption via macOS Keychain or Windows DPAPI as a second way besides the passphrase (the passphrase stays as recovery); spec to justify any new package.
- Block writes without key file: when encrypted files exist but `encryption.json` is missing, refuse commands that write (not just warn) until the key file is restored, and keep the warning visible instead of only on the first frame.
- Bind names, flag plain files: bind each encrypted note's and calendar file's name into its authentication data so encrypted contents can't be swapped between files, and warn about plain files found while encryption is on instead of reading them silently.
- Measure encrypted start-up: time the in-memory tag index rebuild with encryption on for 100, 1,000 and 5,000 notes; only if it is slow, keep an encrypted copy of the index between sessions without ever writing plain data to disk.
- Encrypt while another process writes: if the desktop app (or a second console) writes `context.db` while `/encrypt` runs, a plain `context.db` can be left behind. Make `Migrate` safe against another process writing at the same time (for example, delete `context.db*` again at the end and re-check `IsConfigured`). From the 053 security review.
- Unlock with a password manager: read the encryption passphrase from 1Password (its `op` CLI) or KeePassXC (`keepassxc-cli`) instead of typing it, chosen in the settings (the item's name is stored, never the passphrase); the CLI already takes `--passphrase-stdin`, so document piping `op read` into it. The setup and docs recommend keeping the passphrase in the password manager as recovery, since a lost passphrase loses the data. Decide next to OS key store unlock.
- no echo for --passphrase-stdin on a terminal (from 123-scriptable-cli)
