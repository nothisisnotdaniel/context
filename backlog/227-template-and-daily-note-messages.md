# 227 Template and daily note messages

As a Context user, I want the template and daily note messages to be clear, so that I understand what was created or why it was refused.

Category: ux

## What

- Narrow the daily-note error catch in the desktop app (was #197): In DesktopActions.OpenDailyNoteNow, catch only IOException and UnauthorizedAccessException (as the CLI `context today` does) instead of every Exception. Check whether the other DesktopActions catch-alls should follow, and keep the fixed error text.
- Say when today's note already exists (was #198): When /today (desktop) opens a daily note that already exists, show a grey status line such as "Opened <name>" instead of nothing. Keep the accent "Created <name>" line for a new note. Update the spec text and tests.
- Catch file errors in context new --template (was #199): In Cli.cs NewNoteFromTemplate, move the NoteTemplates.Prepare call (which lists notes to check for an existing one) inside the try block, so an IOException or UnauthorizedAccessException gets the same fixed error line and exit code as the desktop path. Add a test.
- List the template folder once when a template is missing (was #200): In NoteTemplates.TryRead / Prepare, reuse the entries already listed for the missing-template hint instead of enumerating the templates folder a second time.
- Mention --template in the empty /new hint (was #201): When /new is run without a title, change the hint from `Type /new <title>.` to also mention `[--template <name>]`, matching the palette hint. Update the spec text and tests.

## Why

- #197: A catch-all turns any bug into "Can't write <name>. Nothing was done.", which hides the real cause and differs from the CLI.
- #198: Today an existing note opens silently, so it is unclear whether /today did anything or created the page.
- #199: Today an unexpected IO error while listing notes escapes as an unhandled exception on the CLI, while the desktop app shows a fixed message.
- #200: The missing case enumerates the folder twice. It is harmless but wasteful and makes the code harder to follow.
- #201: The palette shows --template, but the hint shown on a blank /new does not, so the feature is easy to miss.

## Notes

- Merges #197, #198, #199, #200, #201 on 2026-11-16, to save the fixed cost of one job per item.
- Cheap mode is enough: message and text changes.
- #197: Proposed by job 164-daily-note (review-code round 1, nit 2). Proposed by job 164-daily-note (`factory/jobs/164-daily-note/`).
- #198: Proposed by job 164-daily-note (review-ux round 1, nit 1). Proposed by job 164-daily-note (`factory/jobs/164-daily-note/`).
- #199: Proposed by job 165-note-templates (review-code round 1, nit 1). Proposed by job 165-note-templates (`factory/jobs/165-note-templates/`).
- #200: Proposed by job 165-note-templates (review-code round 1, nit 2). Proposed by job 165-note-templates (`factory/jobs/165-note-templates/`).
- #201: Proposed by job 165-note-templates (review-ui and review-ux round 1, nit 1). Proposed by job 165-note-templates (`factory/jobs/165-note-templates/`).
