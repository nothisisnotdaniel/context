# 299 Crash log and a hint on the next start

As a desktop user, I want the app to write down why it crashed and tell me so the next time it starts, so that a crash isn't a mystery and I can pass the details on.

## What

The desktop app has no handler for unexpected errors (nothing in src/Context.Desktop/Program.cs or elsewhere hooks AppDomain.UnhandledException, TaskScheduler.UnobservedTaskException or Blazor's unhandled component errors), so a crash leaves no trace. Catch these and write a crash log (time, app version, OS, the exception type, message and stack trace, inner exceptions) to a file in a fixed place, for example a logs folder next to the app's settings, keeping only the last few logs. Note contents and other personal text must not be written to the log beyond what the exception message itself holds; say in the spec how that is limited. On the next start, if a crash log is newer than the last clean start, show a short, dismissable hint such as 'Context closed unexpectedly last time. Crash log: <path>' with a way to open the folder or copy the path. Also say in the spec how a crash in the native web view (Photino) or a hard process kill is handled, for example a 'running' marker file that wasn't cleared on a clean exit. Light and dark mode.

## Why

User report on 2026-11-18: the app sometimes crashes with no idea why.

## Notes

Requested by the user on 2026-11-18. No crash handling exists yet (grep for UnhandledException found nothing).
