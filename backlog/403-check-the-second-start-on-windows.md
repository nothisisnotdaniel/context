# 403 Check the second start on Windows

As the user, I want to check by hand that starting Context twice on Windows brings the open window forward, so that the untested window-to-front call is known to work.

Category: test

## What

On Windows, with Context open, start it again (from the Start menu and from the command line). Check that no second window opens, the first window comes to the front without bad flicker, the second process exits, and no unclean-exit log appears in .logs/. Repeat with the first window minimised.

## Why

Job 315 made a second copy hand over to the open window through a show request, but dotnet test can't check the Photino TopMost toggle that brings the window forward.

## Notes

Proposed by job 315-second-copy-no-crash.
