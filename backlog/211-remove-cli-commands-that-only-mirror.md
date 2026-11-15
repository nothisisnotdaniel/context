# 211 Remove CLI commands that only mirror desktop views

As the Context developer, I want the CLI to keep only commands that scripts or Times use, so that every feature job stops paying to maintain commands nobody runs from a shell.

## What

Remove `context links`, `context people`, `context person` and `context types` (text and `--json`), with their help lines, CliJson output, CliTests and CliJsonTests cases, README sections and PowerShell examples, and their lines in CLAUDE.md's command list and `--json` contract. Keep the Core code they call (LinksView, PersonView, the type pages): the desktop app uses it. An unknown command after removal is the usual usage error. Keep every other command: help, version, capture, todos, todo add/done, notes, note show, tags, find, search, current, review, today, dayend, meeting, new, import and export.

## Why

These four only print what the desktop's links panel, people list, person page and type pages show. No Times story and no planned script uses them, and each one adds work whenever its view changes (for example #185 and #186 exist only for `context person`). The kept ones are what Times reads (`current`, `todos`, `version`), what the command bar, browser extension and scripts call (`capture`, `todo`, `new`, `today`, `dayend`, `meeting`, `import`, `export`) or plain query building blocks for scripts (`notes`, `note show`, `tags`, `find`, `search`, `review`).

## Notes

From the CLI review on 2026-11-15. Removing a `--json` command breaks a public contract, so mention it in the release notes. When this is done, drop #185 and #186 (they only change `context person`).
