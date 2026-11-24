# 471 Check right click in a Release build on Windows

As the developer, I want to check right click in a Release build on Windows, so that users never see the browser menu or dev tools.

Category: test

## What

Run the checklist below on Windows with a Release build and tick each step.

## Checklist

- [x] Start a Release build (Setup.exe, or `dotnet run -c Release --project src/Context.Desktop`).
- [x] Right-click empty space in a panel: no browser menu (Back, Reload, Inspect).
- [ ] Right-click a note link and a chip: no browser menu.
- [x] Right-click a text field (palette, search): the text menu shows, or Ctrl+C and Ctrl+V work.
- [ ] Right-click selected text in the editor: the text menu shows; copy and paste work.
- [x] F12 and Ctrl+Shift+I do nothing: no dev tools.
- [x] If any step fails, add a fix item that names the step and what you saw.

## Why

Job 224 blocks the web view's context menu in script; only a real Release build shows whether it works.

## Notes

- From job 224.
- Split from #308 (one item per OS).

## Windows verification, 2026-11-24

Release at `29b1b32c` showed no menu on a blank sidebar or the linked-notes panel button. Both the inline tag and wiki-link chips opened WebView2's full Back, Forward, Reload, Save as and Print menu. Fix and repeat those two targets in [#516](516-suppress-the-browser-menu-on-non.md). The search field and a mouse-selected editor word showed Cut, Copy and Paste. The selected-editor-text item remains open for an actual copy/paste round trip on an available test clipboard. The existing Windows clipboard contained seven formats and was left untouched.

F12 and Ctrl+Shift+I left the native window list, UI Automation tree and text selection unchanged. On the same input route, Shift+Right selected one character, and Ctrl+Shift+Right selected the first word and space. The Release build kept its ordinary menu and DevTools configuration.
