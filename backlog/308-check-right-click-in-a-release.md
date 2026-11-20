# 308 Check right click in a Release build on macOS and Windows

As the maintainer, I want to right-click in a Release build on macOS and on Windows once, so that I know the browser menu and Inspect are really gone there and text fields still offer Cut, Copy and Paste.

## What

Manual check, left by job 224: build Context.Desktop in Release on macOS and on Windows, right-click on empty space, a note link, an input and the note editor. Expected: no browser menu (Back, Reload, Inspect) anywhere outside text; inputs and the editor still show the platform text menu or at least keep keyboard copy and paste; no dev tools. If it fails, add a fix item.

## Why

Reviewers of job 224 (code and ux) noted that no automated test can open the real web view's context menu, so the Release behaviour on both platforms is unverified until someone tries it.

## Notes

Proposed by job 224-no-context-menu.

Proposed by job 224-no-context-menu (`factory/jobs/224-no-context-menu/`).
