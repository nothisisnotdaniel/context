# 403 Check the second start on Windows

As the user, I want to check by hand that starting Context twice on Windows brings the open window forward, so that the untested window-to-front call is known to work.

Category: test

## What

On Windows, with Context open, start it again (from the Start menu and from the command line). Check that no second window opens, the first window comes to the front without bad flicker, the second process exits, and no unclean-exit log appears in .logs/. Repeat with the first window minimised.

## Why

Job 315 made a second copy hand over to the open window through a show request, but dotnet test can't check the Photino TopMost toggle that brings the window forward.

## Notes

Proposed by job 315-second-copy-no-crash.

## Windows verification, 2026-11-24

On `29b1b32c`, six serial direct executable launches via `cmd.exe` all exited with code 0, each show-request file was consumed, and the running window remained the only Context window. No unclean-exit log was created. In a fresh primary, attempts 1, 4, 6 successfully brought it to the front. It remained behind the responsive comparison window on attempts 3, 5 and remained minimized on attempts 2. A direct native Restore command restored the same windows. Repeat after [#510](510-restore-and-raise-the-existing-windows.md).

The installed Start-menu entry remains a separate check. This machine had no registered Context entry and the private desktop has no Start surface. A synthesized private shortcut could be activated through ShellExecuteEx, and its explicitly isolated wrapper started the second process. This did not exercise a click in the installed Windows Start menu.
