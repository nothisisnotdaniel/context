# 406 Carry a line to the next meeting with - [>]

As a Context user, I want to right-click a checkbox in a meeting note to mark it `- [>]` for the next meeting, so that I can flag what to carry over without moving it under an Open points heading.

## What

- **Marker:** a list line `- [>] text` (also `*`/`+`, any indent, anywhere in the note, outside code fences) is a carry-over checkbox.
- **Right-click:** in the desktop editor, right-clicking a local checkbox (`[ ]`) turns it into `[>]`; right-clicking a `[>]` turns it back into `[ ]`. The change is saved like ticking a checkbox. A done `[x]` and a global `{ }`/`{x}` todo (#407) are left as they are. A left-click on `[>]` ticks it (`[x]`) as usual. The right-click is also reachable from the keyboard (for example a context-menu key or a shortcut on the focused checkbox) and is named for screen readers.
- **Look:** the editor shows `[>]` as a checkbox with an arrow, distinct from `[ ]` and `[x]`, never as text or a quote. Typing `- [>] ` renders it at once, like the other markers (#407).
- **Carry-over:** when the next occurrence's note is created, `[>]` lines are copied into `## From last time` as `- [ ] text`, in line order together with the open `- [ ]` lines, the `Open points` lines and the open `- { }` todos (open `- [ ]` lines still carry over too). A line that is also in an `Open points` section is copied once. The previous note stays unchanged.
- **Not a todo:** `[>]` lines are local, like `[ ]`: they don't show in the todo list, the Todos page, the CLI or the API.
- The README meeting-notes section and the slash-command help mention the marker and the right-click.

## Why

Moving points under an `Open points` heading breaks up the structure of a note. A right-click on the checkbox flags a point for next week in place. `- [>]` is the bullet-journal "migrated" mark (also used by Obsidian Tasks), sits naturally next to `- [ ]` and `- [x]`, and does not clash with Markdown quotes the way `>>` would.

## Notes

Asked for on 2026-11-20; right-click toggle and immediate rendering added the same day. The user chose that open `- [ ]` lines keep carrying over as well. Builds on meeting-note carry-over (job 245, `MeetingCarryOver.Extract`) and on #407's local/global checkboxes in the editor (`editor/src/markdown.js`, `schema.js`, `view.js`). Check that `- [>]` is never rewritten on save.
