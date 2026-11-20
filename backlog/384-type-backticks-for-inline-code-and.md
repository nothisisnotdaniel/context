# 384 Type backticks for inline code and code blocks, never auto-escape input

As the user, I want to type `code` for inline code and ``` for a code block in the editor, so that I can write code from the keyboard without the editor escaping what I typed.

## What

In the desktop note editor, typing text between single backticks (`like this`) turns it into inline code as soon as the closing backtick is typed, and typing ``` at the start of a line (optionally followed by a language, then Enter or space) starts a fenced code block. Today the editor escapes these characters (it saves \` instead). The editor must never escape typed input on its own: what the user types is what the .md file holds, outside the existing tag/link nodes. Add round-trip tests: backticks, asterisks, underscores, brackets, # and other Markdown characters typed as plain text are saved unescaped, and the two input rules produce the inline code and code block nodes from job 283.

## Why

The user asked for it: code blocks and inline code must be reachable from the keyboard, and the editor must never escape input automatically.

## Notes

Requested by the user on 2026-11-19. Builds on job 283 (code blocks and inline code).
