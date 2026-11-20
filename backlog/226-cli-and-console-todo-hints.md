# 226 CLI and console todo hints

As a Context user, I want the CLI and console todo messages and hints to be clear and consistent, so that I know what happened and what to type next.

## What

- List the matching todos when /done is ambiguous (was #157): When `/done <text>` (console) or `context todo done <text>` (CLI) matches more than one todo, print the matching todos (one line each, as the sidebar shows them) under the "N todos match" line. Still remove nothing.
- Same /done usage hint in the console and the CLI (was #158): Make the hint shown for `/done` with no text in the console and for `context todo done` with no text in the CLI use the same wording (adjusting only the command name), and update the goldens.
- Fix the /todo hint in context current (was #159): `context current` still prints `Add one with /todo today <text>.` when there are no todos for today, a console command that no longer exists since the interactive console was removed. Replace it with the matching `context todo` CLI command (or the desktop app), and update the tests that pin the text.
- Show --json as an option in the CLI help (was #165): The help Commands table lists `<command> --json` as if it were a command. Move it under an Options heading (or reword it) and update the help-text test.
- Name the bad todo id in the CLI warning (was #171): When a CLI command repairs a todo whose stored id is not a GUID, name the todo (its text, shortened) in the stderr warning and print it after the command output, so the warning is easy to act on.

## Why

- #157: Raised by the ux review of job 144: "N todos match" never lists the matches, so the user has to look at the sidebar or run `context todos` to work out a unique text.
- #158: Raised by the code review of job 144: the two hints are worded slightly differently after the switch to picking todos by text.
- #159: Raised by the ui, ux, code and security reviews of job 145. Left as is, the hint points users to a command that fails.
- #165: Review-ux round 1 of job 148: a flag listed as a command reads oddly in context help.
- #171: UX review of job 151: the warning comes before the output and does not say which todo was repaired.

## Notes

- Merges #157, #158, #159, #165, #171 on 2026-11-16, to save the fixed cost of one job per item.
- Cheap mode is enough: text and hint changes.
- #157: Proposed by job 144-console-todo-picking (`factory/jobs/144-console-todo-picking/`).
- #158: Proposed by job 144-console-todo-picking (`factory/jobs/144-console-todo-picking/`).
- #159: Proposed by job 145-remove-interactive-console (`factory/jobs/145-remove-interactive-console/`).
- #165: Proposed by job 148-cli-json-output (`factory/jobs/148-cli-json-output/`).
- #171: Proposed by job 151-survive-bad-todo-id (`factory/jobs/151-survive-bad-todo-id/`).
