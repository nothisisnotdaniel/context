# 498 Show the app version and log each update check

As a Context user on Windows, I want to see which version is running and what the last update check did, so that I can tell whether auto-update works.

Category: ux

## What

1) Show the running version (StartupUpdate.CurrentVersion, without the +commit suffix, or "development build" when empty) in the Settings panel, together with the update status: whether this copy updates itself (installed by Setup.exe) or not (portable, macOS, dotnet run), and its channel (develop or stable). 2) StartupUpdate writes one short line per start to a small update log in <data folder>/.logs (next to the crash logs, same size and rotation discipline): time, current version, outcome (not installed / no newer version / downloaded X, applies on exit / failed: exception type and a short message, no stack trace, no tokens or user paths). Show the last outcome under the version in Settings. Keep the check off the UI thread and silent otherwise (no dialog). This changes the rule in CLAUDE.md that the updater never writes to the data folder: update CLAUDE.md and the tests to match, and keep the log free of note content and personal data.

## Why

The user installed the Windows build with Setup.exe and thinks it does not update, but the app shows no version anywhere and StartupUpdate swallows every failure without a trace, so there is no way to tell what happens or why.

## Notes

Reported on 23 Nov 2026 after job 395. StartupUpdate.cs swallows all exceptions by design ("no hint, no log line"). Velopack itself may also log under %LOCALAPPDATA%\ContextDesktop; mention it in build.md if found, but do not depend on it.
