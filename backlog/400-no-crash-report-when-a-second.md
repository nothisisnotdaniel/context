# 400 No crash report when a second copy starts

As the user, I want Context to report a crash only when the app really ended badly, so that starting it twice doesn't file a false crash.

## What

Program.cs calls CrashLog.Begin() before it takes <root>/.desktop/running.lock. A second copy started while the first still runs finds the session file at 'running <start>' and writes an unclean-exit log, then overwrites the marker, so the first copy's clean End() is the only thing that fixes it. Issue #1 on GitHub (Windows, 0.0.1-develop.168, unclean-exit 18 s after the previous start, no exception) fits this. Take the running lock before the crash check; when another copy holds it, skip the unclean-exit check and leave .logs/session untouched. On Windows such a second copy should hand over to the open window (as the command line's open request does) and exit instead of opening a second window over the same data folder.

## Why

A false crash report hides real ones and sends the user looking for a bug that isn't there; two copies writing the same data folder can also clash.

## Notes

Added on 2026-11-20 from Context issue #1. The user doesn't know whether a second start happened.
