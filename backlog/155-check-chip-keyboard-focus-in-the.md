# 155 Check chip keyboard focus in the desktop web view

As the Context maintainer, I want to try chip keyboard focus by hand on macOS and Windows, so that I know the tab stops and focus ring work in the real web view.

## What

In the desktop editor: Tab through a note with tag and link chips; check the focus ring in light and dark themes; Enter opens a focused chip; a mouse click still opens it; Cmd/Ctrl+S and Cmd/Ctrl+K work with a chip focused. On macOS, check whether chips are skipped unless the system keyboard-navigation setting is on.

## Why

Job 142 added tab stops and a focus ring for chips, but no automated test covers the keydown handler or the visual ring, and the reviews flagged the manual checks as not run.
