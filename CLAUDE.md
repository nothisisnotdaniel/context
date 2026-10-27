# Context — conventions

Every factory agent reads this file before working on Context. Keep it short and accurate.

- **What it is:** a personal console app for keeping information in one place. The user stores
  notes as Markdown files in a `Context` folder inside their app data folder
  (`Environment.SpecialFolder.ApplicationData`). Later it will pull in information from email and
  other sources.
- **UI:**
  - An input box at the bottom of the screen is used for navigation.
  - A todo list is always shown in a sidebar, sorted by due date. The user sets each todo's due date.
- **Stack:** .NET console app using Spectre.Console. The first factory job sets up the solution and
  fills in the exact version here.
- **Test command:** none yet.
- **Layout:** where the source code, the tests and the entry points live.
- **Style:** naming, formatting, libraries to prefer or avoid, and anything reviewers should enforce.
- **Git:** the main branch is `main`. Factory work happens on `factory/<job-id>` branches and is merged with `--no-ff`.
