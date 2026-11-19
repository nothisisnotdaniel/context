# 372 Code blocks and inline code in notes

As the user, I want to write code blocks and inline code in a note, so that commands, config and code snippets stay readable and copyable.

## What

The note renderer (src/Context.Core/NoteHtml.cs) already turns fenced blocks into <pre><code> and backticks into <code>, but the editor gives no way to make them and they don't look like code. Add editor actions (toolbar button and shortcut) for inline code and a fenced code block with an optional language, show both in a monospace font with a distinct background in light and dark themes, highlight syntax for common languages in code blocks, and add a copy button on each block. Typing inside a code block must not trigger markdown shortcuts, mentions or tags.

## Why

The user asked for proper code snippets and inline code: notes often hold commands and code, and today they read as plain text.

## Notes

Requested by the user on 2026-11-19.
