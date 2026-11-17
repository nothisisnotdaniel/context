# 257 Web links work in the note editor

As a Context user, I want a Markdown link like [Name](https://url) in a note to show as a clickable link called Name, so that I can open web pages straight from my notes.

## What

In the desktop app's note editor (src/Context.Desktop/editor/src: schema.js, markdown.js, view.js; EditorInterop, DesktopActions, ShellNoteLauncher or a new launcher): (1) `[text](https://…)` shows as a link with only `text` visible, also when it is directly followed by `:` or other punctuation and more text on the line, as in `[Name](https://url): Some more text`. The spec checks why this case does not show as a link today (parse, render or click) and fixes every cause. (2) Clicking the link opens http, https and mailto URLs in the system's default browser or mail app through .NET; the spec decides whether a plain click or Cmd/Ctrl+click opens it while editing, and the link shows a hover title with the URL. Today view.js only opens attachment PDF links on click. (3) Any other scheme (javascript:, file:, data:, relative paths other than attachments) is never opened; the URL is validated in .NET again before it reaches the launcher, and no note text becomes a shell argument. (4) Saving keeps the link text byte for byte (round-trip unchanged), and attachment PDF links keep working as today. (5) Never escape anything on save: today saving a note with such a link wrote a `\` before the `[` and before characters inside the URL. The editor must write what the user typed and add no backslash anywhere (not before `[`, `]`, `(`, `)`, `*`, `_`, `#`, `:` or any other character, in links or in plain text). If the user wants an escape, they type the `\` themselves, and it is kept as typed. (6) A link is visibly different from the surrounding text by colour (an accent link colour, readable in light and dark mode), so it can be told apart without hovering. Light and dark mode.

## Why

Links in notes such as `[Name](https://url): Some more text` don't work in the editor today, so web links in notes are dead text, and saving adds backslashes the user never typed.

## Notes

Requested by the user on 2026-11-17. view.js:20 and :78 only open attachment PDF hrefs on click (attachmentPdf regex). The launcher rule in CLAUDE.md (ShellNoteLauncher only gets checked note or attachment paths) has to be extended on purpose for web URLs; the security reviewer should look at it. 2026-11-17 addition: the user saw `\[` and backslashes inside the URL after saving; the serializer in markdown.js (prosemirror-markdown's esc) is the likely source.
