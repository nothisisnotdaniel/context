# 257 Web links work in the note editor

As a Context user, I want a Markdown link like [Name](https://url) in a note to show as a clickable link called Name, so that I can open web pages straight from my notes.

## What

In the desktop app's note editor (src/Context.Desktop/editor/src: schema.js, markdown.js, view.js; EditorInterop, DesktopActions, ShellNoteLauncher or a new launcher): (1) `[text](https://…)` shows as a link with only `text` visible, also when it is directly followed by `:` or other punctuation and more text on the line, as in `[Name](https://url): Some more text`. The spec checks why this case does not show as a link today (parse, render or click) and fixes every cause. (2) Clicking the link opens http, https and mailto URLs in the system's default browser or mail app through .NET; the spec decides whether a plain click or Cmd/Ctrl+click opens it while editing, and the link shows a hover title with the URL. Today view.js only opens attachment PDF links on click. (3) Any other scheme (javascript:, file:, data:, relative paths other than attachments) is never opened; the URL is validated in .NET again before it reaches the launcher, and no note text becomes a shell argument. (4) Saving keeps the link text byte for byte (round-trip unchanged), and attachment PDF links keep working as today. Light and dark mode.

## Why

Links in notes such as `[Name](https://url): Some more text` don't work in the editor today, so web links in notes are dead text.

## Notes

Requested by the user on 2026-11-17. view.js:20 and :78 only open attachment PDF hrefs on click (attachmentPdf regex). The launcher rule in CLAUDE.md (ShellNoteLauncher only gets checked note or attachment paths) has to be extended on purpose for web URLs; the security reviewer should look at it.
