# 518 Restore and raise the existing Windows window on repeated desktop starts

As a Windows user starting Context again, I want the existing window to return to the front even after earlier handovers.

Category: bug

## What

- Keep a real Windows desktop instance running with another normal window in front. Alternate between leaving Context open and minimizing it through the native window command. Start Context repeatedly via its executable. Each request must restore a minimized primary, bring that same window above the intervening window, and finish the new process without opening a second Context window or logging an unclean exit.
- Trace each consumed show request through OpenRequestWatcher.ShowRequested and the Photino window-thread restore/raise path. Include coverage for consecutive handovers and for a primary that has previously been restored. Verify the result in Windows with both the normal application shortcut and a command line.

## Why

At develop 29b1b32c a fresh Windows primary received six serial cmd.exe launches. All six second processes exited with code 0, and each open-request file was written and consumed. Attempts 1, 4, 6 restored/raised the existing window as expected. Attempts 3, 5 left an ordinary window behind the other window. Attempts 2 left a minimized primary minimized. Each launch was observed for at least six seconds after the second process exited. The direct Windows restore command worked on those same minimized windows.

## Notes

Delegated Windows check #403 for original job #400 / 315. A separate private primary reproduced the difference. Real Start-menu acceptance is still open because Context was not registered on the test system. Opening a synthetic .lnk through the Windows shell was recorded separately.
