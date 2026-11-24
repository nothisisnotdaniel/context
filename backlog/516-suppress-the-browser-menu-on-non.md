# 516 Suppress the browser menu on non-editable chips inside the note editor

As a Windows desktop user, I want right-clicking a tag or an internal note link in the editor to stay inside Context.

Category: bug

## What

- Open a Release build and a note with an inline #tag and [[Note link]]. Right-click each rendered chip. Suppress the native navigation and page commands on both chips, including Back, Forward, Reload, Save as and Print.
- Keep the text-editing context menu when right-clicking selected editable text or a search field, and preserve the deliberate right-click action on a task from #406. Normal click and keyboard activation of chips must still work.
- Cover non-editable inline nodes inside the editable editor in a focused regression test. Repeat the Windows Release checklist in #471 and leave the macOS verification with #470.

## Why

In the Windows Release build at 29b1b32c, right-clicking #release471 or [[QA 471 Target]] opened a native WebView2 menu with Zurück, Weiter, Aktualisieren, Speichern unter and Drucken. A blank sidebar area and the linked-notes panel button showed no browser menu. The search field and selected editor text offered Cut, Copy and Paste.

## Notes

From #471. #292 / job 224 introduced the original right-click behavior. The input/contenteditable exclusion in editor/src/index.js also matches non-editable inline nodes through their editor ancestor.
