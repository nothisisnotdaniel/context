# 407 Global todos as - { }, note-local checkboxes as - [ ]

As a Context user, I want only `- { }` lines to be todos that show outside the note, and `- [ ]` checkboxes to stay local to their note, so that meeting points I tick off in the note don't fill my todo list.

## What

A list line `- { } text` (also `*`/`+`, any indent, outside fences, with the same `due:yyyy-MM-dd` rule) is a note todo and `- {x} text` a done one; they show in the todo list, the Todos page, the person page, briefings and every other place note todos show today, and completing one turns `{ }` into `{x}`. `- [ ]` / `- [x]` lines become plain note checkboxes: they no longer show in any todo view, the CLI or the API, but still carry over to the next occurrence of a recurring meeting (with `- { }` todos, both kept as written). The editor shows `{ }` / `{x}` as a checkbox that looks different from a local one (for example an accent border), and ticking it in the editor writes `{x}`; `[ ]` stays a normal Markdown checkbox. **One-off conversion:** the first time the new version opens a data folder, every `- [ ]` and `- [x]` todo line in every plain note is turned into `- { }` / `- {x}` (only the marker, every other byte kept, through the same safe-save as completing a todo), so no existing todo drops out; it runs once (recorded in the data folder), reports how many lines in how many notes changed, skips and lists notes it can't write, and never touches fenced code or topic lines. README, the `/todo` help and the slash-command help describe both markers.

## Why

Most checkboxes in meeting notes are points for that meeting, not tasks for the whole week, yet today every `- [ ]` lands in the todo views. A separate marker keeps the global list for real todos. Converting existing lines once means nothing that is a todo today silently disappears.

## Notes

Asked for on 2026-11-20; the user chose to convert all existing checkboxes once. Rewrites notes in bulk, so take the full path at high risk: back up or record each change in the history, re-check each line before writing (as `NoteTodos.Complete` does), and test with CRLF, BOM, encrypted and sensitive folders. Touches `NoteTodoParser`, `NoteTodos.Complete`, `MeetingCarryOver.Extract` (keep `[ ]` and add `{ }`), the editor (`editor/src/markdown.js`, `schema.js`) and #406 (`- [>]`). Check #404 (Todos page) for the new marker.
