# 292 No browser context menu on right click in the desktop app

As a desktop user, I want right-clicking in the app not to show the web view's browser menu (Back, Reload, Inspect), so that the app feels like an app and not a web page.

## What

Turn off the default context menu of the desktop web view (Photino), on Windows and macOS, for example through the window's context-menu setting or by preventing the contextmenu event in the page. Text fields and the note editor should keep working for copy and paste through the keyboard; if the platform's text-field menu (Cut, Copy, Paste) can be kept in inputs and the editor, keep it. Release builds must not offer Inspect or dev tools.

## Why

User review on 2026-11-18: remove the context menu of the browser that shows up on right click.

## Notes

Requested by the user on 2026-11-18.
